using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.VectorStore;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Services;

/// <summary>Embeds the query, ranks via <see cref="IVectorStore"/>, hydrates results (SPEC-02 RF-004).</summary>
public sealed class SearchService(
    KnowledgeHubDbContext db,
    IEmbeddingProvider embeddings,
    IVectorStore vectors) : ISearchService
{
    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int topK, Guid? sourceId = null, CancellationToken ct = default)
    {
        var activeSourceIds = sourceId is null
            ? await db.Sources.Where(s => s.IsActive).Select(s => s.Id).ToListAsync(ct)
            : [sourceId.Value];

        var queryVector = await embeddings.EmbedAsync(query, ct);
        var hits = await vectors.SearchAsync(queryVector, embeddings.ModelId, topK, activeSourceIds, ct);
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
                UriReference = byId[h.ChunkId].UriReference
            })
            .ToList();
    }
}
