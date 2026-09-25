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
}

/// <summary>Builds providers lazily and caches them by effective-options
/// signature — a signature change (provider/model/dims/path/key/asymmetry)
/// rebuilds once, warns that existing vectors may need reindex.</summary>
public sealed class EmbeddingProviderResolver(
    IEmbeddingSettingsService settings,
    IHttpClientFactory httpFactory,
    ILogger<EmbeddingProviderResolver> logger) : IEmbeddingProviderResolver
{
    private readonly object _gate = new();
    private string? _signature;
    private IEmbeddingProvider? _provider;

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

                var built = EmbeddingProviderFactory.Create(
                    settings.GetEffectiveOptions(), httpFactory);
                if (_provider is not null)
                    logger.LogWarning(
                        "embedding provider changed ({Old} → {New}) — vectors already stored keep the old dims; existing corpora may need reindex",
                        _signature, signature);
                _provider = built;
                _signature = signature;
                return built;
            }
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
