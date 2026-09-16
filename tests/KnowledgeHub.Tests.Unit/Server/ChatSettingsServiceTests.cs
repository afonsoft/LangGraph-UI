using System.Net;
using System.Text;
using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260916-settings-chat-config RF-002/RF-003/RF-005: store-over-env
// precedence, masked key reporting, snapshot caching/invalidation, and the
// connection probe (test seam via Func<HttpClient>).
public sealed class ChatSettingsServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _services;
    private readonly FakeSecretStore _secrets = new();

    public ChatSettingsServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        var sc = new ServiceCollection();
        sc.AddDbContext<KnowledgeHubDbContext>(o => o.UseSqlite(_conn));
        sc.AddHttpClient();
        _services = sc.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>().Database.EnsureCreated();
    }

    private ChatSettingsService Sut(ChatProviderOptions? env = null, Func<HttpClient>? probe = null) =>
        new(Options.Create(env ?? new ChatProviderOptions()),
            _secrets,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IHttpClientFactory>(),
            NullLogger<ChatSettingsService>.Instance,
            probe);

    private static ChatProviderOptions EnvOpenAi(
        string endpoint = "https://env.test",
        string model = "env-model",
        string? apiKey = null) =>
        new() { Provider = "openai", Endpoint = endpoint, Model = model, ApiKey = apiKey };

    [Fact]
    public async Task Describe_NoConfig_SourceNone()
    {
        var dto = await Sut().DescribeAsync();

        Assert.Equal("none", dto.Provider);
        Assert.Equal("none", dto.Source);
        Assert.Equal("none", dto.ApiKeySource);
        Assert.False(dto.HasApiKey);
        Assert.False(dto.EnvConfigured);
        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public async Task Describe_EnvProvider_SourceEnv()
    {
        var dto = await Sut(EnvOpenAi(apiKey: "sk-env-1234")).DescribeAsync();

        Assert.Equal("openai", dto.Provider);
        Assert.Equal("env", dto.Source);
        Assert.Equal("https://env.test", dto.Endpoint);
        Assert.Equal("env-model", dto.Model);
        Assert.True(dto.HasApiKey);
        Assert.Equal("env", dto.ApiKeySource);
        Assert.Equal("••••1234", dto.ApiKeyHint);
        Assert.True(dto.EnvConfigured);
    }

    [Fact]
    public async Task Save_ThenDescribe_StoreBeatsEnv()
    {
        var sut = Sut(EnvOpenAi(endpoint: "https://env.test", model: "env-model"));

        await sut.SaveAsync("https://store.test", "store-model", apiKey: null);
        var dto = await sut.DescribeAsync();

        Assert.Equal("openai", dto.Provider);
        Assert.Equal("store", dto.Source);
        Assert.Equal("https://store.test", dto.Endpoint);
        Assert.Equal("store-model", dto.Model);
        Assert.True(dto.EnvConfigured);
        Assert.NotNull(dto.UpdatedAt);
    }

    [Fact]
    public async Task EffectiveOptions_StoreEndpointModel_StoreKeyBeatsEnvKey()
    {
        var sut = Sut(EnvOpenAi(apiKey: "sk-env-aaaa"));

        await sut.SaveAsync("https://store.test", "store-model", "sk-store-bbbb");
        var options = sut.GetEffectiveOptions();

        Assert.Equal("openai", options.Provider);
        Assert.Equal("https://store.test", options.Endpoint);
        Assert.Equal("store-model", options.Model);
        Assert.Equal("sk-store-bbbb", options.ApiKey);
        Assert.Equal("chat", _secrets.LastSetProvider);
    }

    [Fact]
    public async Task Save_BlankApiKey_KeepsStoredKey()
    {
        var sut = Sut();

        await sut.SaveAsync("https://a.test", "m1", "sk-first-0001");
        await sut.SaveAsync("https://b.test", "m2", apiKey: "   ");

        var dto = await sut.DescribeAsync();
        Assert.True(dto.HasApiKey);
        Assert.Equal("store", dto.ApiKeySource);
        Assert.Equal("sk-first-0001", sut.GetEffectiveOptions().ApiKey);
    }

    [Fact]
    public async Task RemoveKey_EnvKeyFallsBack()
    {
        var sut = Sut(EnvOpenAi(apiKey: "sk-env-cccc"));

        await sut.SaveAsync("https://store.test", "m", "sk-store-dddd");
        await sut.RemoveKeyAsync();

        Assert.Equal("sk-env-cccc", sut.GetEffectiveOptions().ApiKey);
        var dto = await sut.DescribeAsync();
        Assert.True(dto.HasApiKey);
        Assert.Equal("env", dto.ApiKeySource);
    }

    [Fact]
    public async Task Clear_RemovesRowAndKey_BackToEnv()
    {
        var sut = Sut(EnvOpenAi(apiKey: "sk-env-eeee"));

        await sut.SaveAsync("https://store.test", "m", "sk-store-ffff");
        await sut.ClearAsync();

        var dto = await sut.DescribeAsync();
        Assert.Equal("env", dto.Source);
        Assert.Equal("https://env.test", dto.Endpoint);
        Assert.Equal("env", dto.ApiKeySource);
        Assert.True(_secrets.RemovedChat);
    }

    [Fact]
    public void GetClient_NullWhenProviderNone() =>
        Assert.Null(Sut().GetClient());

    [Fact]
    public void GetClient_CachesSnapshot_UntilInvalidate()
    {
        var sut = Sut(EnvOpenAi());

        var first = sut.GetClient();
        var second = sut.GetClient();
        Assert.NotNull(first);
        Assert.Same(first, second);

        sut.Invalidate();
        var third = sut.GetClient();
        Assert.NotNull(third);
        Assert.NotSame(first, third);
    }

    [Fact]
    public async Task GetClient_RebuildsAfterSave_WithoutRestart()
    {
        var sut = Sut(EnvOpenAi(endpoint: "https://env.test", model: "env-model"));
        Assert.Equal("env-model", sut.GetEffectiveOptions().Model);

        await sut.SaveAsync("https://store.test", "store-model", apiKey: null); // saves → Invalidate()

        Assert.Equal("store-model", sut.GetEffectiveOptions().Model);
        Assert.Equal("https://store.test", sut.GetEffectiveOptions().Endpoint);
    }

    [Fact]
    public async Task Test_BlankEndpoint_RequiresEndpoint()
    {
        var result = await Sut().TestAsync(new TestChatConnectionRequest());

        Assert.False(result.Ok);
        Assert.Equal("endpoint is required", result.Detail);
    }

    [Fact]
    public async Task Test_Probe_ModelListed_TrueAndFalse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"data":[{"id":"m1"},{"id":"m2"}]}""");
        var sut = Sut(probe: () => new HttpClient(handler));

        var listed = await sut.TestAsync(new TestChatConnectionRequest
        {
            Endpoint = "https://stub.local",
            Model = "m2",
            ApiKey = "sk-probe-9999"
        });
        Assert.True(listed.Ok);
        Assert.True(listed.ModelListed);
        Assert.Equal("Bearer sk-probe-9999", handler.LastAuth);

        var absent = await sut.TestAsync(new TestChatConnectionRequest
        {
            Endpoint = "https://stub.local",
            Model = "nope"
        });
        Assert.True(absent.Ok);
        Assert.False(absent.ModelListed);
    }

    [Fact]
    public async Task Test_Probe_NonSuccess_SanitizedDetail()
    {
        var sut = Sut(probe: () => new HttpClient(
            new StubHandler(HttpStatusCode.Unauthorized, """{"error":"bad key"}""")));

        var result = await sut.TestAsync(new TestChatConnectionRequest
        {
            Endpoint = "https://stub.local",
            ApiKey = "sk-wrong"
        });

        Assert.False(result.Ok);
        Assert.Equal("HTTP 401", result.Detail);
    }

    [Fact]
    public async Task Describe_NeverEchoesApiKey()
    {
        var sut = Sut();
        const string secret = "sk-supersecret-315f";
        await sut.SaveAsync("https://store.test", "m", secret);

        var dto = await sut.DescribeAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(dto);

        Assert.DoesNotContain(secret, json);
        Assert.Equal("••••315f", dto.ApiKeyHint);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastAuth { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuth = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeSecretStore : IIntegrationSecretStore
    {
        private readonly Dictionary<string, string> _secrets = new();
        public string? LastSetProvider { get; private set; }
        public bool RemovedChat { get; private set; }

        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.TryGetValue(provider, out var s) ? s : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.TryGetValue(provider, out var s)
                ? new IntegrationSecretInfo(provider, s.Length >= 4 ? s[^4..] : s, DateTimeOffset.UtcNow)
                : (IntegrationSecretInfo?)null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default)
        {
            _secrets[provider] = secret;
            LastSetProvider = provider;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default)
        {
            if (provider == IntegrationProviders.Chat)
                RemovedChat = true;
            return Task.FromResult(_secrets.Remove(provider));
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        _conn.Dispose();
    }
}
