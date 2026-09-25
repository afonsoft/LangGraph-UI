using KnowledgeHub.Server.Settings;
using System.Security.Cryptography;
using System.Text;

namespace KnowledgeHub.Server.Embeddings;

/// <summary>
/// SPEC-20260926-settings-ux-embeddings RF-004: resolves the live
/// <see cref="IEmbeddingProvider"/> from the effective (store-over-env)
/// options. Consumers see a stable singleton facade; edits via
/// /api/settings/embeddings swap the underlying provider without restart.
/// </summary>
public interface IEmbeddingProviderResolver
{
    IEmbeddingProvider Current { get; }

    /// <summary>SPEC-20260926-embeddings-runtime-coherence RF-004: short hash of
    /// the current effective-options signature — embed in cache keys so entries
    /// produced under another endpoint/key/input_type are never reused.</summary>
    string Fingerprint { get; }
}

/// <summary>Builds providers lazily and caches them by effective-options
/// signature — a signature change (provider/model/dims/path/key/asymmetry)
/// rebuilds once, warns that existing vectors may need reindex.</summary>
public sealed class EmbeddingProviderResolver(
    IEmbeddingSettingsService settings,
    IHttpClientFactory httpFactory,
    ILogger<EmbeddingProviderResolver> logger,
    Func<EmbeddingOptions, IEmbeddingProvider>? providerFactory = null) : IEmbeddingProviderResolver
{
    private readonly object _gate = new();
    private string? _signature;
    private IEmbeddingProvider? _provider;

    private IEmbeddingProvider Build(EmbeddingOptions options) =>
        providerFactory?.Invoke(options) ?? EmbeddingProviderFactory.Create(options, httpFactory);

    public IEmbeddingProvider Current
    {
        get
        {
            var signature = Signature(settings.GetEffectiveOptions());
            if (_provider is not null && signature == _signature)
                return _provider;
            lock (_gate)
            {
                if (_provider is not null && signature == _signature)
                    return _provider;

                var built = Build(settings.GetEffectiveOptions());
                var previous = _provider;
                if (previous is not null)
                {
                    logger.LogWarning(
                        "embedding provider changed ({Old} → {New}) — vectors already stored keep the old dims; existing corpora may need reindex",
                        _signature, signature);
                    // SPEC-20260926-embeddings-runtime-coherence RF-005: release
                    // native resources of the previous provider (e.g. ONNX
                    // InferenceSession) — best-effort, off the swap path.
                    _ = Task.Run(() => DisposeQuietly(previous));
                }
                _provider = built;
                _signature = signature;
                return built;
            }
        }
    }

    public string Fingerprint => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Signature(settings.GetEffectiveOptions()))))[..8].ToLowerInvariant();

    private void DisposeQuietly(IEmbeddingProvider old)
    {
        try
        {
            switch (old)
            {
                case IAsyncDisposable ad:
                    ad.DisposeAsync().AsTask().Wait();
                    break;
                case IDisposable d:
                    d.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "failed to dispose replaced embedding provider");
        }
    }

    /// <summary>Stable identity of the embedding-relevant options — any field
    /// that changes vector shape/content forces a rebuild. The key contributes
    /// only a hash (never the secret itself).</summary>
    internal static string Signature(EmbeddingOptions o) =>
        string.Join('|',
            o.Provider, o.Endpoint, o.Model, o.Dimensions, o.ModelPath,
            o.ApiKey is null ? "-" : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(o.ApiKey)))[..12],
            o.Asymmetric.Enabled, o.Asymmetric.QueryPrefix, o.Asymmetric.DocumentPrefix,
            o.QueryInputType, o.DocumentInputType);
}

/// <summary>
/// Stable <see cref="IEmbeddingProvider"/> facade forwarding every call —
/// including the role-aware variants — to <see cref="IEmbeddingProviderResolver.Current"/>.
/// Registered as the singleton <c>IEmbeddingProvider</c> so existing consumers
/// pick up settings edits with zero call-site changes.
/// </summary>
public sealed class DelegatingEmbeddingProvider(IEmbeddingProviderResolver resolver) : IEmbeddingProvider
{
    public string ModelId => resolver.Current.ModelId;
    public int Dimensions => resolver.Current.Dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        resolver.Current.EmbedAsync(text, cancellationToken);

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        resolver.Current.EmbedBatchAsync(texts, cancellationToken);

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default) =>
        resolver.Current.EmbedQueryAsync(text, cancellationToken);

    public Task<float[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken = default) =>
        resolver.Current.EmbedDocumentAsync(text, cancellationToken);

    public Task<IReadOnlyList<float[]>> EmbedDocumentBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        resolver.Current.EmbedDocumentBatchAsync(texts, cancellationToken);
}
