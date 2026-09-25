using KnowledgeHub.Server.Caching;
using KnowledgeHub.Server.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260926-cache-key-consistency: honest per-key eviction (untrack only
/// after backend success), replica propagation via the <c>cache-key:</c> bus
/// topic, and subscriber-side L1 eviction.
/// </summary>
public sealed class CacheKeyConsistencyTests
{
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _data = new();
        public bool ThrowOnRemove { get; set; }

        public byte[]? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _data[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key)
        {
            if (ThrowOnRemove) throw new InvalidOperationException("backend down");
            _data.Remove(key);
        }
        public Task RemoveAsync(string key, CancellationToken token = default)
        { Remove(key); return Task.CompletedTask; }
    }

    private sealed class FakeBus : ICacheInvalidationBus
    {
        public List<string> Published { get; } = [];
        public Task PublishAsync(string topic, CancellationToken ct = default)
        {
            Published.Add(topic);
            return Task.CompletedTask;
        }
        public event EventHandler<string>? Received { add { } remove { } }
    }

    private static (CacheManagerService Svc, FakeDistributedCache Cache, FakeBus Bus) Sut()
    {
        var cache = new FakeDistributedCache();
        var bus = new FakeBus();
        var svc = new CacheManagerService(cache,
            Options.Create(new CacheOptions { Provider = "memory" }),
            NullLogger<CacheManagerService>.Instance, bus: bus);
        return (svc, cache, bus);
    }

    [Fact]
    public async Task RemoveEntry_BackendFails_KeepsTracked_ReturnsError()
    {
        var (svc, cache, _) = Sut();
        const string key = "search:test:fail";
        svc.TrackKey(key, 10);
        cache.ThrowOnRemove = true;

        var result = await svc.RemoveEntryAsync(key);

        Assert.False(result.Removed);
        Assert.True(result.Tracked);
        Assert.NotNull(result.Error);
        // Key stays tracked — the panel must not hide a failed eviction.
        var stats = await svc.GetStatsAsync();
        Assert.Contains(stats.Keys, k => k.Key == key);
    }

    [Fact]
    public async Task RemoveEntry_Success_Untracks_AndPublishesTopic()
    {
        var (svc, _, bus) = Sut();
        const string key = "emb:test:propagate";
        svc.TrackKey(key, 10);

        var result = await svc.RemoveEntryAsync(key);

        Assert.True(result.Removed);
        Assert.True(result.Tracked);
        Assert.Equal($"cache-key:{key}", Assert.Single(bus.Published));
        var stats = await svc.GetStatsAsync();
        Assert.DoesNotContain(stats.Keys, k => k.Key == key);
    }

    [Fact]
    public async Task RemoveEntry_UntrackedKey_ReportsNotTracked()
    {
        var (svc, _, bus) = Sut();

        var result = await svc.RemoveEntryAsync("never:tracked");

        Assert.False(result.Tracked);
        Assert.True(result.Removed);
        // Still published — other replicas may hold the key untracked too.
        Assert.Equal("cache-key:never:tracked", Assert.Single(bus.Published));
    }

    [Fact]
    public async Task Subscriber_CacheKeyTopic_EvictsL1Only()
    {
        var l1 = new MemoryCache(new MemoryCacheOptions());
        var l2 = new FakeDistributedCache();
        var l1l2 = new L1L2Cache(l1, l2, TimeSpan.FromMinutes(5));
        var raiser = new RaisingBus();
        using var subscriber = new InvalidationSubscriber(
            raiser, l1l2, NullLogger<InvalidationSubscriber>.Instance);

        // Prime: this replica's L1 holds a stale copy; L2 still has the real
        // value (publisher removed the key on ITS L2/Redis, so for the test
        // L2 keeps it — we only assert L1 eviction).
        l2.Set("k2", [2]);
        l1.Set("k2", new byte[] { 2 });

        raiser.Raise("cache-key:k2");

        Assert.Null(l1.Get("k2"));
        Assert.NotNull(await l1l2.GetAsync("k2")); // L2 still serves it
    }

    private sealed class RaisingBus : ICacheInvalidationBus
    {
        private EventHandler<string>? _received;
        public void Raise(string topic) => _received?.Invoke(this, topic);
        public Task PublishAsync(string topic, CancellationToken ct = default) => Task.CompletedTask;
        public event EventHandler<string>? Received { add => _received += value; remove => _received -= value; }
    }
}
