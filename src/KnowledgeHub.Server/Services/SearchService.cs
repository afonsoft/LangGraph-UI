using KnowledgeHub.Server.Caching;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Search;
using KnowledgeHub.Server.VectorStore;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// Embeds the query, ranks via <see cref="IVectorStore"/> and/or the FTS5 lexical
/// index, and hydrates results (SPEC-02 RF-004, SPEC-20260914-hybrid-retrieval
/// RF-002: hybrid mode fuses both rankings with RRF k=60 over a topK×4 window).
/// SPEC-20260916-performance-memory-cache RF-005: query embeddings and result
/// sets are cached in <see cref="IDistributedCache"/> — result keys embed the
/// index-version token so any sync invalidates them.
/// </summary>
public sealed class SearchService(
    KnowledgeHubDbContext db,
    IEmbeddingProvider embeddings,
    IVectorStore vectors,
    ILexicalSearchService lexical,
    IDistributedCache cache,
    ILogger<SearchService> logger) : ISearchService
{
    private const int CandidateWindowFactor = 4;
    private static readonly TimeSpan EmbeddingTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResultTtl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int topK, Guid? sourceId = null,
        SearchMode mode = SearchMode.Hybrid, CancellationToken ct = default)
    {
        var indexVersion = await GetIndexVersionAsync(ct);
        var resultKey = CacheKeys.Search(mode.ToString(), topK, sourceId, query, indexVersion);
        var cached = await SafeCache.GetStringAsync(cache, resultKey, logger, ct);
        if (cached is not null)
        {
            var hit = JsonSerializerSafely(cached);
            if (hit is not null)
                return hit;
        }

        var results = await ExecuteAsync(query, topK, sourceId, mode, ct);
        await SafeCache.SetJsonAsync(cache, resultKey, results, ResultTtl, logger, ct);
        return results;
    }

    private async Task<IReadOnlyList<SearchResultItem>> ExecuteAsync(
        string query, int topK, Guid? sourceId,
        SearchMode mode, CancellationToken ct)
    {
        var activeSourceIds = sourceId is null
            ? await db.Sources.Where(s => s.IsActive).Select(s => s.Id).ToListAsync(ct)
            : [sourceId.Value];

        if (mode == SearchMode.Semantic)
        {
            var queryVector = await EmbedQueryAsync(query, ct);
            var hits = await vectors.SearchAsync(queryVector, embeddings.ModelId, topK, activeSourceIds, ct);
            return await HydrateAsync(hits, breakdowns: null, ct);
        }

        var window = topK * CandidateWindowFactor;

        var vectorRanked = mode == SearchMode.Hybrid
            ? (await vectors.SearchAsync(
                await EmbedQueryAsync(query, ct), embeddings.ModelId, window, activeSourceIds, ct))
                .Select(h => h.ChunkId).ToList()
            : [];

        var lexicalRanked = (await lexical.SearchAsync(query, window, activeSourceIds, ct))
            .Select(h => h.ChunkId).ToList();

        var fused = RrfFuser.Fuse(vectorRanked, lexicalRanked, topK);
        if (fused.Count == 0)
            return [];

        var breakdowns = fused.ToDictionary(
            f => f.ChunkId,
            f => new SearchScoreBreakdown
            {
                VectorRank = f.VectorRank,
                LexicalRank = f.LexicalRank,
                Fused = f.Fused
            });

        return await HydrateAsync(
            fused.Select(f => new VectorHit(f.ChunkId, f.Fused)).ToList(), breakdowns, ct);
    }

    /// <summary>Query embedding via the distributed cache — embeddings are
    /// deterministic per (modelId, text) so the key needs no version.</summary>
    private async Task<float[]> EmbedQueryAsync(string query, CancellationToken ct)
    {
        var key = CacheKeys.Embedding(embeddings.ModelId, query);
        var cached = await SafeCache.GetAsync(cache, key, logger, ct);
        if (cached is not null)
            return EmbeddingVectorCodec.FromBytes(cached);

        var vector = await embeddings.EmbedAsync(query, ct);
        await SafeCache.SetAsync(cache, key, EmbeddingVectorCodec.ToBytes(vector), EmbeddingTtl, logger, ct);
        return vector;
    }

    /// <summary>Current index-version token; a missing/unreadable one simply
    /// gets a fresh value (all existing keys miss, which is correct).</summary>
    private async Task<string> GetIndexVersionAsync(CancellationToken ct)
    {
        var version = await SafeCache.GetStringAsync(cache, CacheKeys.IndexVersion, logger, ct);
        if (version is not null)
            return version;
        version = Guid.NewGuid().ToString("N");
        await SafeCache.SetStringAsync(cache, CacheKeys.IndexVersion, version,
            TimeSpan.FromDays(7), logger, ct);
        return version;
    }

    private List<SearchResultItem>? JsonSerializerSafely(string payload)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<SearchResultItem>>(payload);
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // corrupt payload behaves as a miss
        }
    }

    private async Task<IReadOnlyList<SearchResultItem>> HydrateAsync(
        IReadOnlyList<VectorHit> hits,
        IReadOnlyDictionary<Guid, SearchScoreBreakdown>? breakdowns,
        CancellationToken ct)
    {
        if (hits.Count == 0)
            return [];

        var chunkIds = hits.Select(h => h.ChunkId).ToList();
        var chunks = await db.Chunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .Join(db.Documents, c => c.KnowledgeDocumentId, d => d.Id, (c, d) => new { c.Id, c.TextContent, d.Title, d.UriReference, d.KnowledgeSourceId })
            .Join(db.Sources, x => x.KnowledgeSourceId, s => s.Id, (x, s) => new { x.Id, x.TextContent, x.Title, x.UriReference, x.KnowledgeSourceId, SourceName = s.Name })
            .ToListAsync(ct);

        var byId = chunks.ToDictionary(c => c.Id);
        return hits
            .Where(h => byId.ContainsKey(h.ChunkId))
            .Select(h => new SearchResultItem
            {
                ChunkText = byId[h.ChunkId].TextContent,
                DocumentTitle = byId[h.ChunkId].Title,
                SourceName = byId[h.ChunkId].SourceName,
                SourceId = byId[h.ChunkId].KnowledgeSourceId,
                Score = h.Score,
                UriReference = byId[h.ChunkId].UriReference,
                ScoreBreakdown = breakdowns?.GetValueOrDefault(h.ChunkId)
            })
            .ToList();
    }
}
