using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260926-settings-ux-embeddings RF-004: provider cache by options
/// signature — same effective options reuse the instance, any signature change
/// rebuilds; the delegating facade forwards role-aware calls.
/// </summary>
public sealed class EmbeddingProviderResolverTests
{
    private sealed class MutableSettings(EmbeddingOptions options) : IEmbeddingSettingsService
    {
        public EmbeddingOptions Options { get; set; } = options;
        public EmbeddingOptions GetEffectiveOptions() => Options;
        public (int, int) GetChunking() => (500, 50);
        public Task<EmbeddingSettingsDto> DescribeAsync(CancellationToken ct = default) =>
            Task.FromResult<EmbeddingSettingsDto>(null!);
        public Task SaveAsync(SaveEmbeddingSettingsRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task RemoveKeyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Invalidate() { }
    }

    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static EmbeddingProviderResolver Sut(EmbeddingOptions options) =>
        new(new MutableSettings(options), new FakeHttpFactory(),
            NullLogger<EmbeddingProviderResolver>.Instance);

    [Fact]
    public void Current_SameOptions_ReusesInstance()
    {
        var resolver = Sut(new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 });

        var first = resolver.Current;
        var second = resolver.Current;

        Assert.Same(first, second);
        Assert.Equal("deterministic:hash384", first.ModelId);
    }

    [Fact]
    public void Current_OptionsChange_RebuildsProvider()
    {
        var settings = new MutableSettings(new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 });
        var resolver = new EmbeddingProviderResolver(settings, new FakeHttpFactory(),
            NullLogger<EmbeddingProviderResolver>.Instance);

        var before = resolver.Current;
        Assert.Equal(384, before.Dimensions);

        // Swap options → signature change → rebuild.
        settings.Options = new EmbeddingOptions { Provider = "deterministic", Dimensions = 512 };
        var after = resolver.Current;

        Assert.NotSame(before, after);
        Assert.Equal("deterministic:hash512", after.ModelId);
        Assert.Equal(512, after.Dimensions);
    }

    [Fact]
    public void Current_KeyChange_RebuildsProvider()
    {
        var settings = new MutableSettings(new EmbeddingOptions
        {
            Provider = "deterministic",
            Dimensions = 384,
            ApiKey = "sk-a"
        });
        var resolver = new EmbeddingProviderResolver(settings, new FakeHttpFactory(),
            NullLogger<EmbeddingProviderResolver>.Instance);

        var before = resolver.Current;
        settings.Options = new EmbeddingOptions
        {
            Provider = "deterministic",
            Dimensions = 384,
            ApiKey = "sk-b"
        };

        Assert.NotSame(before, resolver.Current);
    }

    [Fact]
    public void Fingerprint_TracksEffectiveOptions()
    {
        var settings = new MutableSettings(new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 });
        var resolver = new EmbeddingProviderResolver(settings, new FakeHttpFactory(),
            NullLogger<EmbeddingProviderResolver>.Instance);

        var fp1 = resolver.Fingerprint;
        Assert.Equal(8, fp1.Length);

        settings.Options = new EmbeddingOptions { Provider = "deterministic", Dimensions = 384, ApiKey = "k" };
        Assert.NotEqual(fp1, resolver.Fingerprint);

        settings.Options = new EmbeddingOptions { Provider = "deterministic", Dimensions = 512 };
        Assert.NotEqual(fp1, resolver.Fingerprint);
    }

    [Fact]
    public async Task Current_Swap_DisposesPreviousProvider()
    {
        // SPEC-20260926-embeddings-runtime-coherence RF-005: providers holding
        // native resources (ONNX InferenceSession) must not leak on swap.
        var settings = new MutableSettings(new EmbeddingOptions
        {
            Provider = "onnx",
            ModelPath = "nonexistent",
            Dimensions = 384
        });
        var created = new List<DisposableStub>();
        var resolver = new EmbeddingProviderResolver(settings, new FakeHttpFactory(),
            NullLogger<EmbeddingProviderResolver>.Instance,
            providerFactory: o => { var d = new DisposableStub(); created.Add(d); return d; });

        var first = resolver.Current;
        settings.Options = new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 };
        var second = resolver.Current;

        Assert.NotSame(first, second);
        await Task.Delay(100); // disposal runs off-path on Task.Run
        Assert.Equal(2, created.Count);
        Assert.True(created[0].Disposed);   // previous released
        Assert.False(created[1].Disposed);  // current alive
    }

    private sealed class DisposableStub : IEmbeddingProvider, IDisposable
    {
        public bool Disposed { get; private set; }
        public string ModelId => "stub";
        public int Dimensions => 4;
        public void Dispose() => Disposed = true;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new float[4]);
    }

    [Fact]
    public async Task DelegatingProvider_ForwardsAllCalls()
    {
        var resolver = Sut(new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 });
        IEmbeddingProvider facade = new DelegatingEmbeddingProvider(resolver);

        Assert.Equal(resolver.Current.ModelId, facade.ModelId);
        Assert.Equal(resolver.Current.Dimensions, facade.Dimensions);

        var vec = await facade.EmbedAsync("hello");
        var doc = await facade.EmbedDocumentAsync("hello");
        var query = await facade.EmbedQueryAsync("hello");
        var batch = await facade.EmbedBatchAsync(["a", "b"]);

        Assert.Equal(384, vec.Length);
        Assert.Equal(384, doc.Length);
        Assert.Equal(384, query.Length);
        Assert.Equal(2, batch.Count);
    }
}
