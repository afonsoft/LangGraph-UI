using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// Unit tests for <see cref="ApiKeyChatSettingsService"/> — SPEC-20260916-api-key-settings RF-002.
/// Covers: per-API-key chat settings (endpoint/model/apiKey), integration key overrides,
/// fallback to global defaults, cache invalidation, and cleanup on removal.
/// </summary>
public sealed class ApiKeyChatSettingsServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _services;
    private readonly FakeSecretStore _secrets = new();

    public ApiKeyChatSettingsServiceTests()
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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ApiKeyChatSettingsService Sut(ChatProviderOptions? globalEnv = null) =>
        new(
            new ChatSettingsService(
                Options.Create(globalEnv ?? new ChatProviderOptions()),
                _secrets,
                _services.GetRequiredService<IServiceScopeFactory>(),
                _services.GetRequiredService<IHttpClientFactory>(),
                NullLogger<ChatSettingsService>.Instance),
            _secrets,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IHttpClientFactory>(),
            NullLogger<ApiKeyChatSettingsService>.Instance);

    private static ChatProviderOptions GlobalOpenAi(
        string endpoint = "https://global.test/v1",
        string model = "global-model") =>
        new() { Provider = "openai", Endpoint = endpoint, Model = model, ApiKey = "global-key" };

    // -------------------------------------------------------------------------
    // DescribeAsync — no per-key override → falls back to global
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DescribeAsync_NoOverride_ReturnsGlobalValues()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut(GlobalOpenAi());

        // When
        var dto = await sut.DescribeAsync(apiKeyId);

        // Then
        Assert.Equal("https://global.test/v1", dto.Endpoint);
        Assert.Equal("global-model", dto.Model);
        Assert.Equal("env", dto.Source);
        Assert.False(dto.HasOverride);
    }

    // -------------------------------------------------------------------------
    // SaveAsync — creates override row for endpoint + model
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_EndpointAndModel_CreatesOverrideRow()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut(GlobalOpenAi());

        // When
        await sut.SaveAsync(apiKeyId, "https://perkey.test/v1", "perkey-model", null);
        var dto = await sut.DescribeAsync(apiKeyId);

        // Then
        Assert.Equal("https://perkey.test/v1", dto.Endpoint);
        Assert.Equal("perkey-model", dto.Model);
        Assert.True(dto.HasOverride);
        Assert.Contains("endpoint", dto.OverrideFields);
        Assert.Contains("model", dto.OverrideFields);
    }

    // -------------------------------------------------------------------------
    // SaveAsync — stores apiKey in secret store
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_WithApiKey_StoresInSecretStore()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut(GlobalOpenAi());

        // When
        await sut.SaveAsync(apiKeyId, null, null, "sk-per-key-secret");

        // Then
        var stored = await _secrets.GetAsync($"apikey-chat-{apiKeyId:N}");
        Assert.Equal("sk-per-key-secret", stored);
    }

    // -------------------------------------------------------------------------
    // SaveAsync — null all fields removes row and secret
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_AllNull_RemovesRowAndSecret()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut(GlobalOpenAi());
        await sut.SaveAsync(apiKeyId, "https://over.test", "over-model", "sk-secret");

        // When: reset with all-null
        await sut.SaveAsync(apiKeyId, null, null, null);
        var dto = await sut.DescribeAsync(apiKeyId);

        // Then: falls back to global
        Assert.Equal("https://global.test/v1", dto.Endpoint);
        Assert.False(dto.HasOverride);
        var secret = await _secrets.GetAsync($"apikey-chat-{apiKeyId:N}");
        Assert.Null(secret);
    }

    // -------------------------------------------------------------------------
    // Invalidate — clears in-memory snapshot cache
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Invalidate_ForcesSnapshotRebuild()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut(GlobalOpenAi());

        // Prime snapshot
        await sut.DescribeAsync(apiKeyId);

        // Save a new override
        await sut.SaveAsync(apiKeyId, "https://new.test", "new-model", null);

        // Invalidate so snapshot is rebuilt
        sut.Invalidate(apiKeyId);

        // When: re-describe (rebuilds from DB)
        var dto = await sut.DescribeAsync(apiKeyId);

        // Then
        Assert.Equal("https://new.test", dto.Endpoint);
        Assert.Equal("new-model", dto.Model);
    }

    // -------------------------------------------------------------------------
    // SaveIntegrationKeyAsync / GetIntegrationSecretAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveIntegrationKey_ThenGetIntegrationSecret_ReturnsPerKeyValue()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut();

        // When
        await sut.SaveIntegrationKeyAsync(apiKeyId, "firecrawl", "fc-per-key-secret");
        var retrieved = await sut.GetIntegrationSecretAsync(apiKeyId, "firecrawl");

        // Then
        Assert.Equal("fc-per-key-secret", retrieved);
    }

    [Fact]
    public async Task GetIntegrationSecretAsync_NoPerKeySecret_FallsBackToGlobal()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut();
        await _secrets.SetAsync("tavily", "global-tavily-key");

        // When: no per-key tavily secret set
        var retrieved = await sut.GetIntegrationSecretAsync(apiKeyId, "tavily");

        // Then: falls back to global key
        Assert.Equal("global-tavily-key", retrieved);
    }

    // -------------------------------------------------------------------------
    // RemoveIntegrationKeyAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveIntegrationKeyAsync_RemovesPerKeySecret()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut();
        await sut.SaveIntegrationKeyAsync(apiKeyId, "deepwiki", "dw-secret");

        // When
        await sut.RemoveIntegrationKeyAsync(apiKeyId, "deepwiki");
        var retrieved = await sut.GetIntegrationSecretAsync(apiKeyId, "deepwiki");

        // Then
        Assert.Null(retrieved);
    }

    // -------------------------------------------------------------------------
    // RemoveAsync — cleans up all secrets and DB row
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_CleansUpAllSecretsAndInvalidatesCache()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut(GlobalOpenAi());
        await sut.SaveAsync(apiKeyId, "https://over.test", "over-model", "sk-secret");
        await sut.SaveIntegrationKeyAsync(apiKeyId, "firecrawl", "fc-secret");

        // When
        await sut.RemoveAsync(apiKeyId);

        // Then: chat secret gone
        Assert.Null(await _secrets.GetAsync($"apikey-chat-{apiKeyId:N}"));
        // Then: firecrawl secret gone
        Assert.Null(await _secrets.GetAsync($"apikey-firecrawl-{apiKeyId:N}"));
        // Then: describe falls back to global
        sut.Invalidate(apiKeyId); // snapshot cleared by RemoveAsync already
        var dto = await sut.DescribeAsync(apiKeyId);
        Assert.False(dto.HasOverride);
    }

    // -------------------------------------------------------------------------
    // DescribeAsync — integration key hints surfaced
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DescribeAsync_WithIntegrationKeySet_ShowsHintInDto()
    {
        // Given
        var apiKeyId = await NewApiKeyAsync();
        var sut = Sut();
        await sut.SaveIntegrationKeyAsync(apiKeyId, "firecrawl", "fc-per-key-1234");

        // When
        var dto = await sut.DescribeAsync(apiKeyId);

        // Then
        Assert.True(dto.IntegrationKeys!["firecrawl"].HasKey);
        Assert.NotNull(dto.IntegrationKeys["firecrawl"].Hint);
    }

    // -------------------------------------------------------------------------
    // Helpers / fakes
    // -------------------------------------------------------------------------


    private async Task<Guid> NewApiKeyAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var user = new KnowledgeHub.Server.Domain.Entities.AppUser
        {
            Username = $"u-{Guid.NewGuid():N}",
            PasswordHash = "x"
        };
        var key = new KnowledgeHub.Server.Domain.Entities.ApiKey
        {
            Name = "test",
            KeyHash = Guid.NewGuid().ToString("N"),
            Prefix = "aft_test",
            User = user
        };
        db.Add(key);
        await db.SaveChangesAsync();
        return key.Id;
    }

    private sealed class FakeSecretStore : IIntegrationSecretStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string provider, CancellationToken ct = default) =>
            Task.FromResult(_data.TryGetValue(provider, out var v) ? v : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken ct = default) =>
            Task.FromResult(_data.TryGetValue(provider, out var v)
                ? new IntegrationSecretInfo(provider, v.Length >= 4 ? v[^4..] : v, DateTimeOffset.UtcNow)
                : (IntegrationSecretInfo?)null);

        public Task SetAsync(string provider, string secret, CancellationToken ct = default)
        {
            _data[provider] = secret;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string provider, CancellationToken ct = default)
        {
            var removed = _data.Remove(provider);
            return Task.FromResult(removed);
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        _conn.Dispose();
    }
}
