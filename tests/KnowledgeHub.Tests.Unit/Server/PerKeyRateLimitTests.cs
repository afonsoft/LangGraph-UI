using System.Security.Claims;
using KnowledgeHub.Server.Auth;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260923-per-key-rate-limits — resolver cache semantics and
/// McpToolRateLimiter per-key override enforcement.
/// </summary>
public sealed class PerKeyRateLimitTests : IDisposable
{
    private sealed class FakeResolver(ApiKeyRateLimitOverride? value) : IApiKeyRateLimitResolver
    {
        public bool TryGetOverride(Guid keyId, out ApiKeyRateLimitOverride? v)
        {
            v = value;
            return value is not null;
        }
        public void Invalidate() { }
    }

    private readonly Microsoft.Data.Sqlite.SqliteConnection _conn;
    private readonly ServiceProvider _provider;

    public PerKeyRateLimitTests()
    {
        _conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var services = new ServiceCollection();
        services.AddDbContext<KnowledgeHubDbContext>(o => o.UseSqlite(_conn));
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    private ApiKeyRateLimitResolver Resolver() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>());

    private async Task<ApiKey> SeedKeyAsync(int? llmPermits = null, int? llmWindow = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var user = new AppUser { Username = $"u-{Guid.NewGuid():N}"[..20], PasswordHash = "x" };
        var key = new ApiKey
        {
            Name = "k",
            KeyHash = $"h-{Guid.NewGuid():N}",
            Prefix = "p",
            User = user,
            LlmRateLimitPermits = llmPermits,
            LlmRateLimitWindowSeconds = llmWindow
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task Resolver_LoadsOnlyRowsWithOverrides()
    {
        var with = await SeedKeyAsync(llmPermits: 3);
        var without = await SeedKeyAsync();

        var sut = Resolver();
        Assert.True(sut.TryGetOverride(with.Id, out var ov));
        Assert.Equal(3, ov!.LlmPermits);
        Assert.False(sut.TryGetOverride(without.Id, out _));
    }

    [Fact]
    public async Task Resolver_Invalidate_ReloadsNewRows()
    {
        var sut = Resolver();
        Assert.False(sut.TryGetOverride(Guid.NewGuid(), out _)); // populate the cache
        var key = await SeedKeyAsync(llmPermits: 7);

        Assert.False(sut.TryGetOverride(key.Id, out _)); // stale cache — row unseen
        sut.Invalidate();
        Assert.True(sut.TryGetOverride(key.Id, out var ov));
        Assert.Equal(7, ov!.LlmPermits);
    }

    private static RateLimitOptions Options() => new()
    {
        Enabled = true,
        LlmPermitLimit = 5,
        AnonymousLlmPermitLimit = 5,
        LlmWindowSeconds = 60,
        SyncPermitLimit = 5,
        SyncWindowSeconds = 60,
        GeneralPermitLimit = 5,
        GeneralWindowSeconds = 60
    };

    private static DefaultHttpContext HttpForKey(Guid keyId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ApiKeyAuthenticationHandler.KeyIdClaim, keyId.ToString())], "test"));
        return ctx;
    }

    [Fact]
    public void Limiter_KeyWithOverride_UsesCustomPermits()
    {
        var keyId = Guid.NewGuid();
        var resolver = new FakeResolver(new ApiKeyRateLimitOverride(1, null, null, null));
        using var sut = new McpToolRateLimiter(Options(), resolver, NullLogger<McpToolRateLimiter>.Instance);
        var http = HttpForKey(keyId);

        Assert.True(sut.TryAcquire("ask_knowledge", http, out _));   // 1 of 1
        Assert.False(sut.TryAcquire("ask_knowledge", http, out _));  // over
    }

    [Fact]
    public void Limiter_KeyWithoutOverride_UsesGlobalPermits()
    {
        var resolver = new FakeResolver(null);
        using var sut = new McpToolRateLimiter(Options(), resolver, NullLogger<McpToolRateLimiter>.Instance);
        var http = HttpForKey(Guid.NewGuid());

        for (var i = 0; i < 5; i++)
            Assert.True(sut.TryAcquire("ask_knowledge", http, out _));
        Assert.False(sut.TryAcquire("ask_knowledge", http, out _));
    }

    [Fact]
    public void Limiter_OverrideFingerprint_IsolatesBuckets()
    {
        var keyId = Guid.NewGuid();
        var resolver = new FakeResolver(new ApiKeyRateLimitOverride(1, null, null, null));
        using var sut = new McpToolRateLimiter(Options(), resolver, NullLogger<McpToolRateLimiter>.Instance);
        var http = HttpForKey(keyId);

        Assert.True(sut.TryAcquire("ask_knowledge", http, out _)); // 1/1 custom
        Assert.False(sut.TryAcquire("ask_knowledge", http, out _));

        // A different override value would land on a different fingerprint —
        // simulate by swapping resolver to another value and verifying the
        // first call succeeds again (fresh partition).
        using var sut2 = new McpToolRateLimiter(Options(), new FakeResolver(
            new ApiKeyRateLimitOverride(3, null, null, null)), NullLogger<McpToolRateLimiter>.Instance);
        for (var i = 0; i < 3; i++)
            Assert.True(sut2.TryAcquire("ask_knowledge", http, out _));
        Assert.False(sut2.TryAcquire("ask_knowledge", http, out _));
    }
}
