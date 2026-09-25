using KnowledgeHub.Server.Caching;
using KnowledgeHub.Server.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260926-redis-stats-admin-and-connflag: connectivity follows PING;
/// SCAN/INFO failures degrade to <c>StatsError</c> — never "desconectado" while
/// the server is healthy. Gated on KH_TEST_REDIS (live Redis).
/// </summary>
public sealed class RedisStatsTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("KH_TEST_REDIS");

    private static IConnectionMultiplexer Mux(string conn, bool allowAdmin)
    {
        var parsed = ConfigurationOptions.Parse(conn);
        parsed.AbortOnConnectFail = false;
        parsed.ConnectTimeout = 3000;
        parsed.AllowAdmin = allowAdmin;
        return ConnectionMultiplexer.Connect(parsed);
    }

    private static CacheManagerService Svc(IConnectionMultiplexer mux) =>
        new(
            new RedisCache(Options.Create(new RedisCacheOptions())),
            Options.Create(new CacheOptions { Provider = "redis" }),
            NullLogger<CacheManagerService>.Instance,
            redis: mux);

    [Fact]
    public async Task GetStats_EnrichmentFailure_KeepsIsConnectedTrue_SetsStatsError()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return; // gated: KH_TEST_REDIS

        // Regression for the reported bug: mux WITHOUT AllowAdmin — INFO throws
        // "unless admin mode is enabled" inside the enrichment. Before the fix
        // the catch flipped IsConnected=false ("desconectado" com Redis saudável).
        await using var mux = Mux(Conn, allowAdmin: false);
        var stats = await Svc(mux).GetStatsAsync();

        Assert.True(stats.IsConnected);
        Assert.NotNull(stats.StatsError);
    }

    [Fact]
    public async Task GetStats_WithAdmin_PopulatesServerStats()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return; // gated: KH_TEST_REDIS

        await using var mux = Mux(Conn, allowAdmin: true);
        var stats = await Svc(mux).GetStatsAsync();

        Assert.True(stats.IsConnected);
        Assert.True(stats.ServerReported);
        Assert.NotNull(stats.ServerUsedMemoryBytes);
        Assert.Null(stats.StatsError);
    }

    [Fact]
    public async Task GetStats_UnreachableRedis_IsConnectedFalse()
    {
        // Unroutable endpoint — no env needed. Ping must fail the connectivity
        // verdict (this is the ONLY thing that should flip the badge).
        await using var mux = Mux("localhost:1", allowAdmin: true);
        var stats = await Svc(mux).GetStatsAsync();

        Assert.False(stats.IsConnected);
    }
}
