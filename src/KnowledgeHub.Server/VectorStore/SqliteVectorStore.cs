using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.VectorStore;

/// <summary>
/// Default zero-infra store: vectors live as BLOBs on <c>DocumentChunks</c>;
/// cosine similarity is computed in-process (SPEC-02 RF-006).
/// </summary>
public sealed class SqliteVectorStore(KnowledgeHubDbContext db) : IVectorStore
{
    public async Task UpsertAsync(Guid chunkId, Guid documentId, Guid sourceId, float[] vector, string model, CancellationToken cancellationToken = default)
    {
        var chunk = await db.Chunks.FindAsync([chunkId], cancellationToken)
            ?? throw new InvalidOperationException($"Chunk {chunkId} not found");
        chunk.Embedding = EmbeddingVectorCodec.ToBytes(vector);
        chunk.EmbeddingModel = model;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var chunks = await db.Chunks
            .Where(c => c.KnowledgeDocumentId == documentId)
            .ToListAsync(cancellationToken);
        foreach (var chunk in chunks)
        {
            chunk.Embedding = null;
            chunk.EmbeddingModel = null;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] queryVector, string model, int topK,
        IReadOnlyCollection<Guid>? sourceIds = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.Chunks
            .Where(c => c.Embedding != null && c.EmbeddingModel == model)
            .Join(db.Documents, c => c.KnowledgeDocumentId, d => d.Id, (c, d) => new { c.Id, c.Embedding, d.KnowledgeSourceId });

        if (sourceIds is { Count: > 0 })
            query = query.Where(x => sourceIds.Contains(x.KnowledgeSourceId));
        else if (sourceIds is { Count: 0 })
            return [];

        var rows = await query.AsNoTracking().ToListAsync(cancellationToken);

        return rows
            .Select(r => new VectorHit(r.Id, EmbeddingVectorCodec.CosineSimilarity(queryVector, EmbeddingVectorCodec.FromBytes(r.Embedding!))))
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }
}
