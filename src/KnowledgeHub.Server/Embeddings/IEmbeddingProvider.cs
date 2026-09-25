namespace KnowledgeHub.Server.Embeddings;

/// <summary>
/// Pluggable text-embedding provider (SPEC-02 RF-004 / SPEC-03).
/// Selected via <c>Embeddings:Provider</c> config; implementations:
/// deterministic (offline default), ollama, openai-compatible.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Identity stamped on stored vectors, e.g. "ollama:nomic-embed-text".</summary>
    string ModelId { get; }

    /// <summary>Vector length produced by this provider.</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], cancellationToken);
        return results;
    }

    /// <summary>SPEC-20260924-asymmetric-embeddings RF-001: embed text in the
    /// <b>query</b> role. Asymmetric providers (nomic, e5, bge, Voyage) apply
    /// their query prefix/input_type; symmetric providers inherit the default.</summary>
    Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default) =>
        EmbedAsync(text, cancellationToken);

    /// <summary>Embed text in the <b>document</b> role (ingestion path).</summary>
    Task<float[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken = default) =>
        EmbedAsync(text, cancellationToken);

    /// <summary>Batch-embed documents in the document role.</summary>
    async Task<IReadOnlyList<float[]>> EmbedDocumentBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            results[i] = await EmbedDocumentAsync(texts[i], cancellationToken);
        return results;
    }
}
