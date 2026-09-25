namespace KnowledgeHub.Server.VectorStore;

/// <summary>One ranked hit from vector search.</summary>
public sealed record VectorHit(Guid ChunkId, double Score);

/// <summary>One vector to persist in a batch upsert. <paramref name="Metadata"/>
/// is an optional key/value map persisted by stores that support it (pgvector
/// <c>metadata jsonb</c>); other stores ignore it
/// (SPEC-20260923-pgvector-metadata-upsert RF-001).</summary>
public sealed record VectorUpsert(
    Guid ChunkId, Guid DocumentId, Guid SourceId, float[] Vector,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Abstraction over chunk-embedding storage and similarity search (SPEC-02 RF-006).
/// <c>sqlite</c> (default): embeddings in DocumentChunks, cosine in-process.
/// <c>postgres</c>: pgvector column + server-side &lt;=&gt; distance.
/// </summary>
public interface IVectorStore
{
    /// <summary>Store or replace the vector for a chunk, stamped with the producing model id.
    /// <paramref name="metadata"/> is persisted where supported (pgvector jsonb); ignored elsewhere.</summary>
    Task UpsertAsync(Guid chunkId, Guid documentId, Guid sourceId, float[] vector, string model,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch upsert (SPEC-20260916-performance-memory-cache RF-004) — default
    /// loops <see cref="UpsertAsync"/>; stores with cheaper bulk paths override it.
    /// </summary>
    async Task UpsertBatchAsync(
        IReadOnlyList<VectorUpsert> items, string model, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
            await UpsertAsync(item.ChunkId, item.DocumentId, item.SourceId, item.Vector, model, item.Metadata, cancellationToken);
    }

    /// <summary>Purge all vectors owned by a document.</summary>
    Task DeleteByDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge all vectors owned by a source
    /// (SPEC-20260925-pgvector-source-cascade RF-001) — external stores keep
    /// vectors in their own table where EF cascade cannot reach; without this,
    /// deleting a source orphans its rows forever.
    /// </summary>
    Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rank chunks by similarity to <paramref name="queryVector"/>.
    /// Only vectors whose stored model equals <paramref name="model"/> are eligible.
    /// <paramref name="sourceIds"/> restricts the search space (active sources); null = all.
    /// </summary>
    Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] queryVector, string model, int topK,
        IReadOnlyCollection<Guid>? sourceIds = null,
        CancellationToken cancellationToken = default);
}
