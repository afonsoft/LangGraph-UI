namespace KnowledgeHub.Server.VectorStore;

/// <summary>One ranked hit from vector search.</summary>
public sealed record VectorHit(Guid ChunkId, double Score);

/// <summary>
/// Abstraction over chunk-embedding storage and similarity search (SPEC-02 RF-006).
/// <c>sqlite</c> (default): embeddings in DocumentChunks, cosine in-process.
/// <c>postgres</c>: pgvector column + server-side &lt;=&gt; distance.
/// </summary>
public interface IVectorStore
{
    /// <summary>Store or replace the vector for a chunk, stamped with the producing model id.</summary>
    Task UpsertAsync(Guid chunkId, Guid documentId, Guid sourceId, float[] vector, string model, CancellationToken cancellationToken = default);

    /// <summary>Purge all vectors owned by a document.</summary>
    Task DeleteByDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

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
