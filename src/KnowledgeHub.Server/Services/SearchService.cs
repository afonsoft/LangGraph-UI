using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Search;
using KnowledgeHub.Server.VectorStore;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// Embeds the query, ranks via <see cref="IVectorStore"/> and/or the FTS5 lexical
/// index, and hydrates results (SPEC-02 RF-004, SPEC-20260914-hybrid-retrieval
/// RF-002: hybrid mode fuses both rankings with RRF k=60 over a topK×4 window).
/// </summary>
public sealed class SearchService(
    KnowledgeHubDbContext db,
    IEmbeddingProvider embeddings,
    IVectorStore vectors,
    ILexicalSearchService lexical) : ISearchService
{
    private const int CandidateWindowFactor = 4;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int topK, Guid? sourceId = null,
        SearchMode mode = SearchMode.Hybrid, CancellationToken ct = default)
    {
        var activeSourceIds = sourceId is null
            ? await db.Sources.Where(s => s.IsActive).Select(s => s.Id).ToListAsync(ct)
            : [sourceId.Value];

        if (mode == SearchMode.Semantic)
        {
            var queryVector = await embeddings.EmbedAsync(query, ct);
            var hits = await vectors.SearchAsync(queryVector, embeddings.ModelId, topK, activeSourceIds, ct);
            return await HydrateAsync(hits, breakdowns: null, ct);
        }

        var window = topK * CandidateWindowFactor;

        var vectorRanked = mode == SearchMode.Hybrid
            ? (await vectors.SearchAsync(
                await embeddings.EmbedAsync(query, ct), embeddings.ModelId, window, activeSourceIds, ct))
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
