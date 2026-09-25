using System.Collections.Concurrent;
using KnowledgeHub.Server.Configuration;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Server.Caching;

/// <summary>
/// Default implementation of <see cref="ICacheManagerService"/> (SPEC-20260924-redis-cache-and-tool-caching).
/// Tracks active keys, sizes, expiration times and provides cache stats and clear operations.
/// </summary>
public sealed class CacheManagerService : ICacheManagerService
{
    private sealed record CacheEntryMeta(string Key, string Prefix, long SizeBytes, DateTimeOffset? ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntryMeta> _trackedKeys = new(StringComparer.Ordinal);
    private readonly IDistributedCache _cache;
    private readonly IMemoryCache? _memoryCache;
    private readonly CacheOptions _options;
    private readonly ILogger<CacheManagerService> _logger;
    private readonly StackExchange.Redis.IConnectionMultiplexer? _redis;
    private readonly ICacheInvalidationBus? _bus;
    private long _hits;
    private long _misses;

    public static ICacheManagerService? Current { get; private set; }

    public CacheManagerService(
        IDistributedCache cache,
        IOptions<CacheOptions> options,
        ILogger<CacheManagerService> logger,
        IMemoryCache? memoryCache = null,
        StackExchange.Redis.IConnectionMultiplexer? redis = null,
        ICacheInvalidationBus? bus = null)
    {
        _cache = cache;
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
        _redis = redis;
        _bus = bus;
        Current = this;
    }

    public void TrackKey(string key, long sizeBytes, TimeSpan? ttl = null)
    {
        var prefix = ExtractPrefix(key);
        var expiresAt = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : (DateTimeOffset?)null;
        _trackedKeys[key] = new CacheEntryMeta(key, prefix, sizeBytes, expiresAt);
    }

    public void RemoveKey(string key)
    {
        _trackedKeys.TryRemove(key, out _);
    }

    public void RecordHit(bool hit)
    {
        if (hit) Interlocked.Increment(ref _hits);
        else Interlocked.Increment(ref _misses);
    }

    /// <summary>SPEC-20260925-redis-health-and-scan-stats RF-003: SCAN caps —
    /// bounded page size and total keys so stats never stall the endpoint.</summary>
    private const int ScanMaxKeys = 500;
    private const int ScanPageSize = 200;
    private static readonly TimeSpan RedisPingTimeout = TimeSpan.FromSeconds(2);

    public async Task<CacheStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        PruneExpired();

        var provider = _options.Provider.ToLowerInvariant();
        var keysList = _trackedKeys.Values
            .OrderByDescending(k => k.SizeBytes)
            .Take(1000)
            .Select(k => new CacheKeyItemDto
            {
                Key = k.Key,
                Prefix = k.Prefix,
                SizeBytes = k.SizeBytes,
                ExpiresInSeconds = k.ExpiresAt.HasValue
                    ? Math.Max(0, (long)(k.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalSeconds)
                    : null
            })
            .ToList();

        var totalSize = _trackedKeys.Values.Sum(k => k.SizeBytes);
        var stats = new CacheStatsDto
        {
            Provider = provider,
            IsConnected = true,
            TotalKeys = _trackedKeys.Count,
            TotalSizeBytes = totalSize,
            Hits = _hits,
            Misses = _misses,
            Keys = keysList
        };

        // RF-002: with redis, overlay server-side stats — tracked keys reflect
        // only this process; SCAN/INFO report the shared cache truth.
        if (provider == "redis" && _redis is not null)
            await EnrichFromRedisAsync(stats, ct);

        return stats;
    }

    private async Task EnrichFromRedisAsync(CacheStatsDto stats, CancellationToken ct)
    {
        try
        {
            var ping = _redis!.GetDatabase().PingAsync();
            var completed = await Task.WhenAny(ping, Task.Delay(RedisPingTimeout, ct));
            stats.IsConnected = completed == ping && !ping.IsFaulted;

            var server = _redis.GetEndPoints()
                .Select(ep => _redis.GetServer(ep))
                .FirstOrDefault(s => s.IsConnected);
            if (server is null)
                return;

            // Bounded SCAN (never KEYS *) — cap both page size and total count.
            var count = 0L;
            var truncated = false;
            foreach (var _ in server.Keys(pattern: "*", pageSize: ScanPageSize))
            {
                if (++count >= ScanMaxKeys) { truncated = true; break; }
            }
            stats.ServerKeys = count;
            stats.Partial = truncated;
            stats.ServerReported = true;

            var info = await server.InfoAsync("memory");
            var clients = await server.InfoAsync("clients");
            stats.ServerUsedMemoryBytes = ParseInfoLong(info, "used_memory");
            stats.ServerConnectedClients = (int?)ParseInfoLong(clients, "connected_clients");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            stats.IsConnected = false;
            _logger.LogDebug(ex, "redis stats enrichment failed — returning process-local stats");
        }
    }

    private static long? ParseInfoLong(
        IGrouping<string, KeyValuePair<string, string>>[] info,
        string key)
    {
        foreach (var section in info)
            foreach (var kv in section)
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(kv.Value, out var v))
                    return v;
        return null;
    }

    public async Task<ClearCacheResultDto> ClearAllAsync(CancellationToken ct = default)
    {
        var count = _trackedKeys.Count;
        foreach (var key in _trackedKeys.Keys)
        {
            try
            {
                await _cache.RemoveAsync(key, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove key {Key} from cache", key);
            }
        }

        _trackedKeys.Clear();

        // Invalidate index version to cascade-invalidate all versioned cache entries
        try
        {
            await SafeCache.SetStringAsync(_cache, CacheKeys.IndexVersion,
                Guid.NewGuid().ToString("N"), null, _logger, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bump index version during cache clear");
        }

        if (_memoryCache is MemoryCache mc)
        {
            mc.Compact(1.0); // clears all compactable entries
        }

        // SPEC-20260925-distributed-invalidation-pubsub RF-002/RF-003: tell the
        // other replicas to drop their L1 tiers too.
        if (_bus is not null)
            await _bus.PublishAsync("cache-clear", ct);

        _logger.LogInformation("Cache completely cleared: {Count} keys purged", count);
        return new ClearCacheResultDto
        {
            Success = true,
            ClearedKeys = count,
            Message = $"Cache completamente limpo ({count} chaves purgadas com sucesso)."
        };
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _trackedKeys)
        {
            if (kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value <= now)
            {
                _trackedKeys.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static string ExtractPrefix(string key)
    {
        var idx = key.IndexOf(':');
        if (idx <= 0) return "general";
        var second = key.IndexOf(':', idx + 1);
        return second > 0 ? key[..second] : key[..idx];
    }
}
