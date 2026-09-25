using KnowledgeHub.Server.Caching;
using KnowledgeHub.Server.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// Unit tests for <see cref="CacheManagerService"/> (SPEC-20260924-redis-cache-and-tool-caching).
/// </summary>
public sealed class CacheManagerServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IDistributedCache MakeCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static CacheManagerService MakeService(
        IDistributedCache? cache = null,
        string provider = "memory") =>
        new(
            cache ?? MakeCache(),
            Options.Create(new CacheOptions { Provider = provider }),
            NullLogger<CacheManagerService>.Instance);

    // -------------------------------------------------------------------------
    // TrackKey / RemoveKey / GetStatsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TrackKey_ThenGetStats_ReturnsTrackedEntry()
    {
        // Given
        var svc = MakeService();

        // When
        svc.TrackKey("mcp:tool:knowledge_search:abc", 512, TimeSpan.FromHours(1));
        var stats = await svc.GetStatsAsync();

        // Then — cross-test pollution via the static `Current` is possible when
        // parallel tests write through SafeCache; assert only on OUR key.
        var entry = Assert.Single(stats.Keys, k => k.Key == "mcp:tool:knowledge_search:abc");
        Assert.Equal(512, entry.SizeBytes);
    }

    [Fact]
    public async Task RemoveKey_RemovesTrackedEntry()
    {
        // Given
        var svc = MakeService();
        svc.TrackKey("mcp:search:key1", 100);
        svc.TrackKey("mcp:search:key2", 200);

        // When
        svc.RemoveKey("mcp:search:key1");
        var stats = await svc.GetStatsAsync();

        // Then (filtered: parallel tests can leak keys via static Current)
        Assert.DoesNotContain(stats.Keys, k => k.Key == "mcp:search:key1");
        Assert.Equal(200, Assert.Single(stats.Keys, k => k.Key == "mcp:search:key2").SizeBytes);
    }

    [Fact]
    public async Task GetStats_PrunesExpiredKeys()
    {
        // Given: key with a TTL already elapsed (zero = expired at insertion —
        // deterministic; a 1ms+Delay combo races clock granularity on CI)
        var svc = MakeService();
        svc.TrackKey("expired:key", 100, TimeSpan.Zero);
        await Task.Delay(1);

        // When
        svc.TrackKey("live:key", 200, TimeSpan.FromHours(1));
        var stats = await svc.GetStatsAsync();

        // Then: expired key pruned, live key kept (assert own keys only —
        // parallel tests can leak keys into the tracked set via static Current)
        Assert.DoesNotContain(stats.Keys, k => k.Key == "expired:key");
        Assert.Contains(stats.Keys, k => k.Key == "live:key");
    }

    [Fact]
    public async Task GetStats_ReturnsProviderName()
    {
        // Given
        var svc = MakeService(provider: "redis");

        // When
        var stats = await svc.GetStatsAsync();

        // Then
        Assert.Equal("redis", stats.Provider);
    }

    [Fact]
    public async Task GetStats_TotalSizeBytes_SumsAllTrackedKeys()
    {
        // Given
        var svc = MakeService();
        svc.TrackKey("key:a", 100);
        svc.TrackKey("key:b", 250);
        svc.TrackKey("key:c", 150);

        // When
        var stats = await svc.GetStatsAsync();

        // Then — filter to our prefix: unrelated keys from parallel tests may
        // land here through the static Current handle.
        Assert.Equal(500,
            stats.Keys.Where(k => k.Key.StartsWith("key:", StringComparison.Ordinal))
                .Sum(k => k.SizeBytes));
    }

    // -------------------------------------------------------------------------
    // RecordHit (via GetStats Hits/Misses)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecordHit_TrueAndFalse_AccumulatesCorrectly()
    {
        // Given
        var svc = MakeService();

        // When
        svc.RecordHit(true);
        svc.RecordHit(true);
        svc.RecordHit(false);

        var stats = await svc.GetStatsAsync();

        // Then
        Assert.Equal(2, stats.Hits);
        Assert.Equal(1, stats.Misses);
    }

    // -------------------------------------------------------------------------
    // ClearAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearAllAsync_RemovesAllTrackedKeys()
    {
        // Given
        var cache = MakeCache();
        var svc = MakeService(cache);
        svc.TrackKey("mcp:tool:a", 100);
        svc.TrackKey("mcp:tool:b", 200);

        // When
        var result = await svc.ClearAllAsync();

        // Then
        Assert.True(result.Success);
        Assert.Equal(2, result.ClearedKeys);

        var statAfter = await svc.GetStatsAsync();
        // The IndexVersion bump re-tracks that key; our original tool keys must be gone
        Assert.DoesNotContain(statAfter.Keys, k => k.Key == "mcp:tool:a");
        Assert.DoesNotContain(statAfter.Keys, k => k.Key == "mcp:tool:b");
    }

    [Fact]
    public async Task ClearAllAsync_BumpsIndexVersion_InDistributedCache()
    {
        // Given
        var cache = MakeCache();
        var svc = MakeService(cache);

        // Set an initial index version
        await cache.SetStringAsync(CacheKeys.IndexVersion, "old-version");

        // When
        await svc.ClearAllAsync();

        // Then: index version should be bumped (different from original)
        var newVersion = await cache.GetStringAsync(CacheKeys.IndexVersion);
        Assert.NotNull(newVersion);
        Assert.NotEqual("old-version", newVersion);
    }

    [Fact]
    public async Task ClearAllAsync_WithZeroKeys_ReturnsSuccess()
    {
        // Given: empty cache manager
        var svc = MakeService();

        // When
        var result = await svc.ClearAllAsync();

        // Then
        Assert.True(result.Success);
        Assert.Equal(0, result.ClearedKeys);
    }

    // -------------------------------------------------------------------------
    // Prefix extraction (via stats Keys[].Prefix)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("mcp:tool:knowledge_search:abc123", "mcp:tool")]
    [InlineData("mcp:search:v1:hash", "mcp:search")]
    [InlineData("kh:embedding:hash", "kh:embedding")]
    [InlineData("nocodon", "general")]
    [InlineData("single:part", "single")]
    public async Task TrackKey_PrefixExtractedCorrectly(string key, string expectedPrefix)
    {
        // Given
        var svc = MakeService();
        svc.TrackKey(key, 1);

        // When
        var stats = await svc.GetStatsAsync();

        // Then
        var item = Assert.Single(stats.Keys);
        Assert.Equal(expectedPrefix, item.Prefix);
    }

    // -------------------------------------------------------------------------
    // ExpiresInSeconds in stats
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStats_KeyWithTtl_ExpiresInSecondsIsApproximatelyCorrect()
    {
        // Given
        var svc = MakeService();
        svc.TrackKey("tool:a", 100, TimeSpan.FromHours(2));

        // When
        var stats = await svc.GetStatsAsync();

        // Then: should be approximately 7200s (within 5s tolerance)
        var item = Assert.Single(stats.Keys);
        Assert.NotNull(item.ExpiresInSeconds);
        Assert.InRange(item.ExpiresInSeconds!.Value, 7190, 7205);
    }

    [Fact]
    public async Task GetStats_KeyWithoutTtl_ExpiresInSecondsIsNull()
    {
        // Given
        var svc = MakeService();
        svc.TrackKey("tool:a", 100, ttl: null);

        // When
        var stats = await svc.GetStatsAsync();

        // Then
        var item = Assert.Single(stats.Keys);
        Assert.Null(item.ExpiresInSeconds);
    }
}
