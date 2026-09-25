namespace KnowledgeHub.Server.Embeddings;

/// <summary>
/// SPEC-20260924-asymmetric-embeddings RF-002: decorator that applies role
/// prefixes to query/document texts before delegating to the inner provider.
/// Prefixes come from explicit config or are auto-detected from the model name
/// (nomic → "search_query:"/"search_document:", e5 → "query:"/"passage:",
/// bge → query instruction only). A text that already carries the prefix is
/// never double-prefixed.
/// </summary>
public sealed class AsymmetricEmbeddingProvider : IEmbeddingProvider, IDisposable, IAsyncDisposable
{
    private readonly IEmbeddingProvider _inner;
    private readonly string _queryPrefix;
    private readonly string _documentPrefix;

    public AsymmetricEmbeddingProvider(IEmbeddingProvider inner, EmbeddingOptions options)
    {
        _inner = inner;
        var asym = options.Asymmetric;
        var (autoQ, autoD) = asym.Auto ? AutoPrefixes(options.Model) : ("", "");
        _queryPrefix = asym.QueryPrefix ?? autoQ;
        _documentPrefix = asym.DocumentPrefix ?? autoD;
    }

    /// <summary>Carries a "+asym" marker: enabling role prefixes produces
    /// different document vectors, so the model stamp must differ — existing
    /// embeddings mismatch and are excluded/warned by
    /// <see cref="EmbeddingCompatibilityCheck"/> until re-synced (RF-004).</summary>
    public string ModelId => $"{_inner.ModelId}+asym";
    public int Dimensions => _inner.Dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        _inner.EmbedAsync(text, cancellationToken);

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        _inner.EmbedBatchAsync(texts, cancellationToken);

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default) =>
        _inner.EmbedAsync(ApplyPrefix(_queryPrefix, text), cancellationToken);

    public Task<float[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken = default) =>
        _inner.EmbedAsync(ApplyPrefix(_documentPrefix, text), cancellationToken);

    public Task<IReadOnlyList<float[]>> EmbedDocumentBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        _inner.EmbedBatchAsync(
            texts.Select(t => ApplyPrefix(_documentPrefix, t)).ToList(), cancellationToken);

    /// <summary>Fingerprint of the embedding configuration — persisted alongside
    /// the index so a config change can warn that reindex is needed.</summary>
    public string ConfigFingerprint => $"{_inner.ModelId}|{_inner.Dimensions}|q:{_queryPrefix}|d:{_documentPrefix}";

    private static string ApplyPrefix(string prefix, string text) =>
        string.IsNullOrEmpty(prefix) || text.StartsWith(prefix, StringComparison.Ordinal)
            ? text
            : prefix + text;

    private static (string Query, string Document) AutoPrefixes(string? model)
    {
        var m = model?.ToLowerInvariant() ?? "";
        if (m.Contains("nomic"))
            return ("search_query: ", "search_document: ");
        if (m.Contains("e5"))
            return ("query: ", "passage: ");
        if (m.Contains("bge"))
            return ("Represent this sentence for searching relevant passages: ", "");
        return ("", "");
    }

    /// <summary>SPEC-20260926-embeddings-runtime-coherence RF-005: forward
    /// disposal to the wrapped provider (e.g. ONNX InferenceSession) when the
    /// resolver swaps providers.</summary>
    public void Dispose() => (_inner as IDisposable)?.Dispose();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_inner is IAsyncDisposable ad) await ad.DisposeAsync();
        else (_inner as IDisposable)?.Dispose();
    }
}
