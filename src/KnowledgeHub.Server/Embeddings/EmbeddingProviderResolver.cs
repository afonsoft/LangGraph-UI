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

    /// <summary>RF-003 (SPEC-20260926-embeddings-swap-safety): scoped access to
    /// the live provider — hold the lease across the whole call; a swap drains
    /// outstanding leases before disposing the previous provider.</summary>
    EmbeddingLease Acquire();
}

/// <summary>Handle on the live provider — disposing releases the lease.</summary>
public sealed class EmbeddingLease(IEmbeddingProvider provider, Action onRelease) : IDisposable
{
    private Action? _onRelease = onRelease;
    public IEmbeddingProvider Provider { get; } = provider;
    public void Dispose() => Interlocked.Exchange(ref _onRelease, null)?.Invoke();
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
    private LeaseCounter _counter = new();

    /// <summary>RF-003: cap on how long a swap waits for in-flight leases —
    /// an ONNX embed is sub-second; 30 s is generous headroom, after which we
    /// dispose anyway rather than leak the session forever.</summary>
    private static readonly TimeSpan DisposeGrace = TimeSpan.FromSeconds(30);

    private sealed class LeaseCounter
    {
        private int _refs;
        private bool _closed;
        private readonly TaskCompletionSource _drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryAcquire()
        {
            lock (this) { if (_closed) return false; _refs++; return true; }
        }

        public void Release()
        {
            lock (this) { if (--_refs == 0 && _closed) _drained.TrySetResult(); }
        }

        public Task CloseAndDrainAsync()
        {
            lock (this) { _closed = true; if (_refs == 0) _drained.TrySetResult(); }
            return _drained.Task;
        }
    }

    private IEmbeddingProvider Build(EmbeddingOptions options) =>
        providerFactory?.Invoke(options) ?? EmbeddingProviderFactory.Create(options, httpFactory);

    public EmbeddingLease Acquire()
    {
        _ = Current; // ensure a provider exists / signature fresh
        while (true)
        {
            EmbeddingLease? lease = null;
            lock (_gate) // pair provider+counter atomically — a swap swaps both
            {
                var counter = _counter;
                if (counter.TryAcquire())
                    lease = new EmbeddingLease(_provider!, () => counter.Release());
            }
            if (lease is not null)
                return lease;
            _ = Current; // swap in progress — the counter closed; retry on the new one
        }
    }

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
                var previousCounter = _counter;
                if (previous is not null)
                {
                    logger.LogWarning(
                        "embedding provider changed ({Old} → {New}) — vectors already stored keep the old dims; existing corpora may need reindex",
                        _signature, signature);
                    // SPEC-20260926-embeddings-runtime-coherence RF-005 +
                    // SPEC-20260926-embeddings-swap-safety RF-003: drain
                    // outstanding leases (in-flight ONNX inference) before
                    // releasing native resources — disposing a session mid-Run
                    // crashes the caller. Best-effort, off the swap path.
                    _ = Task.Run(async () =>
                    {
                        await Task.WhenAny(previousCounter.CloseAndDrainAsync(),
                            Task.Delay(DisposeGrace));
                        DisposeQuietly(previous);
                    });
                }
                _provider = built;
                _counter = new LeaseCounter();
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
            // RF-002 (SPEC-20260926-embeddings-swap-safety): Auto flips the
            // effective prefix derivation — omitting it meant toggling the
            // flag changed neither the provider nor the fingerprint.
            o.Asymmetric.Enabled, o.Asymmetric.Auto,
            o.Asymmetric.QueryPrefix, o.Asymmetric.DocumentPrefix,
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
    public string ModelId { get { using var lease = resolver.Acquire(); return lease.Provider.ModelId; } }
    public int Dimensions { get { using var lease = resolver.Acquire(); return lease.Provider.Dimensions; } }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        using var lease = resolver.Acquire();
        return await lease.Provider.EmbedAsync(text, cancellationToken);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        using var lease = resolver.Acquire();
        return await lease.Provider.EmbedBatchAsync(texts, cancellationToken);
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        using var lease = resolver.Acquire();
        return await lease.Provider.EmbedQueryAsync(text, cancellationToken);
    }

    public async Task<float[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken = default)
    {
        using var lease = resolver.Acquire();
        return await lease.Provider.EmbedDocumentAsync(text, cancellationToken);
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        using var lease = resolver.Acquire();
        return await lease.Provider.EmbedDocumentBatchAsync(texts, cancellationToken);
    }
}
