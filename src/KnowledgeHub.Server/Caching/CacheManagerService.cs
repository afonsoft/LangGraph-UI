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
    private long _hits;
    private long _misses;

    public static ICacheManagerService? Current { get; private set; }

    public CacheManagerService(
        IDistributedCache cache,
        IOptions<CacheOptions> options,
        ILogger<CacheManagerService> logger,
        IMemoryCache? memoryCache = null)
    {
        _cache = cache;
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
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

    public Task<CacheStatsDto> GetStatsAsync(CancellationToken ct = default)
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

        return Task.FromResult(new CacheStatsDto
        {
            Provider = provider,
            IsConnected = true,
            TotalKeys = _trackedKeys.Count,
            TotalSizeBytes = totalSize,
            Hits = _hits,
            Misses = _misses,
            Keys = keysList
        });
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
                Guid.NewGuid().ToString("N"), TimeSpan.FromDays(7), _logger, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bump index version during cache clear");
        }

        if (_memoryCache is MemoryCache mc)
        {
            mc.Compact(1.0); // clears all compactable entries
        }

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
