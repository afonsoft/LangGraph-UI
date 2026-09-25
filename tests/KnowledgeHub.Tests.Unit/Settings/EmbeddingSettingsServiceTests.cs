using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Settings;

/// <summary>
/// SPEC-20260926-settings-ux-embeddings RF-004: store-over-env precedence,
/// masked key reporting, snapshot invalidation and chunking knob resolution.
/// </summary>
public sealed class EmbeddingSettingsServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _services;
    private readonly FakeSecretStore _secrets = new();

    public EmbeddingSettingsServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        var sc = new ServiceCollection();
        sc.AddDbContext<KnowledgeHubDbContext>(o => o.UseSqlite(_conn));
        _services = sc.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>().Database.EnsureCreated();
    }

    private EmbeddingSettingsService Sut(EmbeddingOptions? env = null,
        Dictionary<string, string?>? config = null,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache? cache = null,
        KnowledgeHub.Server.Caching.ICacheInvalidationBus? bus = null) =>
        new(Options.Create(env ?? new EmbeddingOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(),
            _secrets,
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmbeddingSettingsService>.Instance,
            cache, bus);

    private static EmbeddingOptions EnvOllama(string? apiKey = null) => new()
    {
        Provider = "ollama",
        Endpoint = "http://env-ollama:11434",
        Model = "env-model",
        Dimensions = 768,
        ApiKey = apiKey
    };

    [Fact]
    public async Task Describe_EnvOnly_SourceEnv()
    {
        var dto = await Sut(EnvOllama("sk-env-9999")).DescribeAsync();

        Assert.Equal("ollama", dto.Provider);
        Assert.Equal("env", dto.Source);
        Assert.Equal("env-model", dto.Model);
        Assert.Equal(768, dto.Dimensions);
        Assert.True(dto.HasApiKey);
        Assert.Equal("env", dto.ApiKeySource);
        Assert.Equal("••••9999", dto.ApiKeyHint);
        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public async Task Save_ThenDescribe_StoreOverridesEnv()
    {
        var sut = Sut(EnvOllama());
        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "openai",
            Endpoint = "https://store.test",
            Model = "store-model",
            Dimensions = 1024,
            MaxTokens = 800,
            OverlapTokens = 80,
            ApiKey = "sk-store-abcd"
        });

        var dto = await sut.DescribeAsync();

        Assert.Equal("openai", dto.Provider);
        Assert.Equal("store", dto.Source);
        Assert.Equal("https://store.test", dto.Endpoint);
        Assert.Equal("store-model", dto.Model);
        Assert.Equal(1024, dto.Dimensions);
        Assert.Equal("store", dto.ApiKeySource);
        Assert.Equal("••••abcd", dto.ApiKeyHint);
        Assert.Equal(800, dto.MaxTokens);
        Assert.Equal(80, dto.OverlapTokens);
        Assert.NotNull(dto.UpdatedAt);
    }

    [Fact]
    public async Task EffectiveOptions_StoreRow_OverridesEnvButKeepsAdvancedEnvKnobs()
    {
        var env = EnvOllama();
        env.Asymmetric.Enabled = true;
        env.Asymmetric.QueryPrefix = "query:";
        var sut = Sut(env);
        await sut.SaveAsync(new SaveEmbeddingSettingsRequest { Provider = "deterministic", Dimensions = 256 });

        var opts = sut.GetEffectiveOptions();

        Assert.Equal("deterministic", opts.Provider);
        Assert.Equal(256, opts.Dimensions);
        Assert.Null(opts.Model);
        // Advanced knobs stay env-driven — the row doesn't carry them.
        Assert.True(opts.Asymmetric.Enabled);
        Assert.Equal("query:", opts.Asymmetric.QueryPrefix);
    }

    [Fact]
    public async Task Chunking_StoreRowWins_ThenEnvDefaults()
    {
        var sut = Sut(config: new()
        {
            ["Ingestion:MaxTokens"] = "700",
            ["Ingestion:OverlapTokens"] = "70"
        });

        // No row → env values.
        Assert.Equal((700, 70), sut.GetChunking());

        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "deterministic",
            MaxTokens = 900,
            OverlapTokens = 90
        });
        Assert.Equal((900, 90), sut.GetChunking());
    }

    [Fact]
    public async Task BlankApiKey_KeepsStored()
    {
        var sut = Sut();
        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "openai",
            ApiKey = "sk-first-1111"
        });
        Assert.Equal("sk-first-1111", await _secrets.GetAsync(IntegrationProviders.Embeddings));

        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "openai",
            Model = "m2",
            ApiKey = null
        });
        Assert.Equal("sk-first-1111", await _secrets.GetAsync(IntegrationProviders.Embeddings));
    }

    [Fact]
    public async Task Clear_RestoresEnv()
    {
        var sut = Sut(EnvOllama());
        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "deterministic",
            Dimensions = 128,
            ApiKey = "sk-x-0000"
        });
        Assert.Equal("store", (await sut.DescribeAsync()).Source);

        await sut.ClearAsync();
        var dto = await sut.DescribeAsync();
        Assert.Equal("env", dto.Source);
        Assert.Equal("ollama", dto.Provider);
        Assert.Null(await _secrets.GetAsync(IntegrationProviders.Embeddings));
    }

    [Fact]
    public async Task Invalidate_RebuildsSnapshot()
    {
        var sut = Sut();
        _ = sut.GetEffectiveOptions(); // loads snapshot

        // Mutate the store out-of-band, then invalidate.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            db.EmbeddingSettings.Add(new KnowledgeHub.Server.Domain.Entities.EmbeddingSettings
            {
                Id = 1,
                Provider = "onnx",
                ModelPath = "models/m"
            });
            await db.SaveChangesAsync();
        }

        // Snapshot cached → still env.
        Assert.Equal("deterministic", sut.GetEffectiveOptions().Provider);

        sut.Invalidate();
        Assert.Equal("onnx", sut.GetEffectiveOptions().Provider);
    }

    public void Dispose()
    {
        _services.Dispose();
        _conn.Dispose();
    }

    /// <summary>SPEC-20260926-embeddings-runtime-coherence RF-003: signature
    /// change on Save bumps index:version and publishes on the bus so cached
    /// search/answer/tool results and replica L1 tokens are dropped.</summary>
    [Fact]
    public async Task Save_SignatureChange_BumpsIndexVersion_AndPublishes()
    {
        var cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var bus = new FakeBus();
        var sut = Sut(new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 },
            cache: cache, bus: bus);

        await cache.SetStringAsync("index:version", "v-old");
        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "deterministic",
            Dimensions = 256
        });

        var version = await cache.GetStringAsync("index:version");
        Assert.NotEqual("v-old", version);
        // index-version on signature change; settings-changed on every mutation
        // (SPEC-20260926-embeddings-swap-safety RF-004).
        Assert.Equal(["index-version", "settings-changed"], bus.Published);
    }

    [Fact]
    public async Task Save_NoSignatureChange_PublishesOnlySettingsChanged()
    {
        var bus = new FakeBus();
        var sut = Sut(new EmbeddingOptions { Provider = "deterministic", Dimensions = 384 },
            bus: bus);

        // Same values as env → signature identical → no index-version churn,
        // but settings-changed still fires (RF-004).
        await sut.SaveAsync(new SaveEmbeddingSettingsRequest
        {
            Provider = "deterministic",
            Dimensions = 384
        });

        Assert.Equal("settings-changed", Assert.Single(bus.Published));
    }

    private sealed class FakeBus : KnowledgeHub.Server.Caching.ICacheInvalidationBus
    {
        public List<string> Published { get; } = [];
        public Task PublishAsync(string topic, CancellationToken cancellationToken = default)
        {
            Published.Add(topic);
            return Task.CompletedTask;
        }
        public event EventHandler<string>? Received { add { } remove { } }
    }

    /// <summary>Store de segredos em memória para isolar o serviço sob teste.</summary>
    private sealed class FakeSecretStore : IIntegrationSecretStore
    {
        private readonly Dictionary<string, string> _secrets = new();

        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.TryGetValue(provider, out var s) ? s : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.TryGetValue(provider, out var s)
                ? new IntegrationSecretInfo(provider, s.Length >= 4 ? s[^4..] : s, DateTimeOffset.UtcNow)
                : (IntegrationSecretInfo?)null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default)
        {
            _secrets[provider] = secret;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.Remove(provider));
    }
}
