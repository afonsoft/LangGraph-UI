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
    IConfiguration configuration,
    IQueryRewriter rewriter,
    IReranker reranker,
    Auth.ICallerScopeProvider callerScope,
    ILogger<SearchService> logger) : ISearchService
{
    private const int CandidateWindowFactor = 4;
    private static readonly TimeSpan EmbeddingTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResultTtl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int topK, Guid? sourceId = null,
        SearchMode mode = SearchMode.Hybrid, ResolvedSearchFilter? filter = null,
        CancellationToken ct = default)
    {
        var scope = await callerScope.GetAsync(ct);
        var indexVersion = await GetIndexVersionAsync(ct);
        var resultKey = CacheKeys.Search(
            mode.ToString(), topK, sourceId, filter?.Fingerprint() ?? "-",
            scope.SourceFingerprint, query, indexVersion);
        var cached = await SafeCache.GetStringAsync(cache, resultKey, logger, ct);
        if (cached is not null)
        {
            var hit = JsonSerializerSafely(cached);
            if (hit is not null)
                return hit;
        }

        var results = await ExecuteAsync(query, topK, sourceId, mode, filter, scope, ct);
        await SafeCache.SetJsonAsync(cache, resultKey, results, ResultTtl, logger, ct);
        return results;
    }

    private async Task<IReadOnlyList<SearchResultItem>> ExecuteAsync(
        string query, int topK, Guid? sourceId,
        SearchMode mode, ResolvedSearchFilter? filter,
        Auth.CallerScope scope, CancellationToken ct)
    {
        var activeSourceIds = sourceId is null
            ? await db.Sources.Where(s => s.IsActive).Select(s => s.Id).ToListAsync(ct)
            : [sourceId.Value];

        // SPEC-20260923-source-authorization RF-003: intersect with the key's
        // source scope before any vector/lexical call — never retrieve-then-
        // filter. An explicit sourceId outside scope yields an empty result
        // (not an error) and a SourceScopeDenied audit event.
        if (scope.AllowedSourceIds is { } allowed)
        {
            if (sourceId is { } requested && !allowed.Contains(requested))
                await Auth.ScopeAudit.RecordAsync(
                    db, scope.ApiKeyId, Auth.ScopeAudit.SourceDenied, sourceId: requested, ct: ct);
            activeSourceIds = activeSourceIds.Where(allowed.Contains).ToList();
        }
        if (activeSourceIds.Count == 0)
            return [];

        // RF-001/RF-002: rewrite + rerank run on the user's retrieval intent.
        // Lexical skips rewriting unless explicitly opted in.
        var effectiveQuery = mode == SearchMode.Lexical
            && !configuration.GetValue("Search:QueryRewrite:LexicalToo", false)
            ? query
            : await rewriter.RewriteAsync(query, ct);

        var rerankEnabled = configuration.GetValue("Search:Rerank:Enabled", false);
        var filtersActive = filter is { IsEmpty: false };
        var window = topK * CandidateWindowFactor;
        var rerankCap = configuration.GetValue("Search:Rerank:MaxCandidates", 20);

        // Fetch a wider window when filters or rerank need room to work;
        // otherwise hydrate exactly topK — identical to the pre-filter pipeline.
        var fetchLimit = rerankEnabled || filtersActive
            ? (rerankEnabled ? Math.Min(window, Math.Max(rerankCap, topK)) : window)
            : topK;

        List<VectorHit> windowed;
        IReadOnlyDictionary<Guid, SearchScoreBreakdown>? breakdowns = null;

        if (mode == SearchMode.Semantic)
        {
            var queryVector = await EmbedQueryAsync(effectiveQuery, ct);
            windowed = (await vectors.SearchAsync(queryVector, embeddings.ModelId, fetchLimit, activeSourceIds, ct)).ToList();
        }
        else
        {
            var vectorRanked = mode == SearchMode.Hybrid
                ? (await vectors.SearchAsync(
                    await EmbedQueryAsync(effectiveQuery, ct), embeddings.ModelId, window, activeSourceIds, ct))
                    .Select(h => h.ChunkId).ToList()
                : [];

            var lexicalRanked = (await lexical.SearchAsync(effectiveQuery, window, activeSourceIds, ct))
                .Select(h => h.ChunkId).ToList();

            var fused = RrfFuser.Fuse(vectorRanked, lexicalRanked, fetchLimit);
            if (fused.Count == 0)
                return [];

            breakdowns = fused.ToDictionary(
                f => f.ChunkId,
                f => new SearchScoreBreakdown
                {
                    VectorRank = f.VectorRank,
                    LexicalRank = f.LexicalRank,
                    Fused = f.Fused
                });
            windowed = fused.Select(f => new VectorHit(f.ChunkId, f.Fused)).ToList();
        }

        var items = await HydrateAsync(windowed, breakdowns, filter, ct);
        if (!rerankEnabled || items.Count <= 1)
            return items.Take(topK).ToList();

        return await RerankAsync(query, items, breakdowns, topK, ct);
    }

    /// <summary>RF-002: re-orders the hydrated window by rerank score; any
    /// failure preserves the fused order (fail-open).</summary>
    private async Task<IReadOnlyList<SearchResultItem>> RerankAsync(
        string query, List<SearchResultItem> items,
        IReadOnlyDictionary<Guid, SearchScoreBreakdown>? breakdowns, int topK, CancellationToken ct)
    {
        IReadOnlyList<RerankScore> scores;
        try
        {
            scores = await reranker.RerankAsync(query, items, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reranker failed — returning fused order");
            return items.Take(topK).ToList();
        }

        if (scores.Count == 0)
            return items.Take(topK).ToList();

        var scoreById = scores.ToDictionary(s => s.ChunkId, s => s.Score);
        var order = items.Select((item, i) => (item, i)).ToList();
        order.Sort((a, b) =>
        {
            var sa = a.item.ChunkId is { } id && scoreById.TryGetValue(id, out var s) ? s : double.NegativeInfinity;
            var sb = b.item.ChunkId is { } id2 && scoreById.TryGetValue(id2, out var s2) ? s2 : double.NegativeInfinity;
            var cmp = sb.CompareTo(sa);
            return cmp != 0 ? cmp : a.i.CompareTo(b.i); // stable: fused order on ties
        });

        return order.Take(topK).Select(x =>
        {
            var rerank = x.item.ChunkId is { } id && scoreById.TryGetValue(id, out var s) ? s : (double?)null;
            var bd = x.item.ScoreBreakdown ?? new SearchScoreBreakdown();
            return x.item with
            {
                ScoreBreakdown = new SearchScoreBreakdown
                {
                    VectorRank = bd.VectorRank,
                    LexicalRank = bd.LexicalRank,
                    Fused = bd.Fused,
                    Rerank = rerank
                }
            };
        }).ToList();
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

    /// <summary>Current index-version token — shared helper so the answer
    /// cache keys invalidate on the same signal (RF-003).</summary>
    private Task<string> GetIndexVersionAsync(CancellationToken ct) =>
        IndexVersionToken.GetAsync(cache, logger, ct);

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

    private async Task<List<SearchResultItem>> HydrateAsync(
        IReadOnlyList<VectorHit> hits,
        IReadOnlyDictionary<Guid, SearchScoreBreakdown>? breakdowns,
        ResolvedSearchFilter? filter,
        CancellationToken ct)
    {
        if (hits.Count == 0)
            return [];

        var chunkIds = hits.Select(h => h.ChunkId).ToList();
        // RF-003: sourceType/pathPrefix filter in SQL; indexedAfter filters in
        // memory (SQLite cannot compare DateTimeOffset); language filters the
        // derived metadata map (documents carry no language column yet).
        var query = db.Chunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .Join(db.Documents, c => c.KnowledgeDocumentId, d => d.Id,
                (c, d) => new { c.Id, c.TextContent, c.SuspicionFlags, c.ChunkKind, c.SymbolPath, DocId = d.Id, d.Title, d.UriReference, d.IndexedAt, d.KnowledgeSourceId })
            .Join(db.Sources, x => x.KnowledgeSourceId, s => s.Id,
                (x, s) => new { x.Id, x.TextContent, x.SuspicionFlags, x.ChunkKind, x.SymbolPath, x.DocId, x.Title, x.UriReference, x.IndexedAt, x.KnowledgeSourceId, SourceName = s.Name, s.SourceType });
        if (filter?.SourceType is { } st)
            query = query.Where(x => x.SourceType == st);
        if (filter?.PathPrefix is { } pp)
            query = query.Where(x => x.UriReference.StartsWith(pp));
        var chunks = await query.ToListAsync(ct);

        if (filter?.IndexedAfter is { } ia)
            chunks = chunks.Where(x => x.IndexedAt >= ia).ToList();

        var byId = chunks.ToDictionary(c => c.Id);
        // SPEC-20260923-prompt-injection-guard RF-004: flagged chunks are dropped
        // post-rank when ExcludeFlagged is on (default true) — ranking window is
        // unaffected, the answer just never sees them.
        var excludeFlagged = configuration.GetValue("Security:Injection:ExcludeFlagged", true);
        var excluded = 0;
        var items = hits
            .Where(h => byId.ContainsKey(h.ChunkId))
            .Select(h =>
            {
                var c = byId[h.ChunkId];
                var flagged = c.SuspicionFlags is not null;
                if (flagged && excludeFlagged)
                    excluded++;
                if (flagged && excludeFlagged)
                    return null;
                // RF-003: language is metadata-derived (no column) — a set
                // filter keeps only items whose metadata carries a match.
                if (filter?.Language is { } lang)
                {
                    var meta = BuildMetadata(c.SourceType, c.UriReference, c.ChunkKind, c.SymbolPath);
                    if (!meta.TryGetValue("language", out var l) ||
                        !l.Equals(lang, StringComparison.OrdinalIgnoreCase))
                        return null;
                }
                return new SearchResultItem
                {
                    ChunkText = c.TextContent,
                    DocumentTitle = c.Title,
                    SourceName = c.SourceName,
                    SourceId = c.KnowledgeSourceId,
                    SourceType = c.SourceType,
                    Score = h.Score,
                    UriReference = c.UriReference,
                    ScoreBreakdown = breakdowns?.GetValueOrDefault(h.ChunkId),
                    SuspicionFlags = c.SuspicionFlags,
                    ChunkId = c.Id,
                    DocumentId = c.DocId,
                    Metadata = BuildMetadata(c.SourceType, c.UriReference, c.ChunkKind, c.SymbolPath),
                    IndexedAt = c.IndexedAt
                };
            })
            .OfType<SearchResultItem>()
            .ToList();

        if (excluded > 0)
            logger.LogInformation("Excluded {Count} flagged chunk(s) from search context", excluded);
        return items;
    }

    /// <summary>RF-004: provenance metadata derived from stored columns.</summary>
    private static IReadOnlyDictionary<string, string> BuildMetadata(
        SourceType sourceType, string uri, string chunkKind, string? symbolPath)
    {
        var meta = new Dictionary<string, string>
        {
            ["sourceType"] = sourceType.ToString(),
            ["path"] = uri,
            ["chunkKind"] = chunkKind
        };
        if (symbolPath is not null)
            meta["symbolPath"] = symbolPath;
        return meta;
    }
}
