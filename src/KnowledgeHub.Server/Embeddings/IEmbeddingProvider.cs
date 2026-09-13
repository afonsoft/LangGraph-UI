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
}
