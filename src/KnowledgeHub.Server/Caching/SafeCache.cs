using System.Text.Json;
using KnowledgeHub.Server.Telemetry;
using Microsoft.Extensions.Caching.Distributed;

namespace KnowledgeHub.Server.Caching;

/// <summary>
/// Fail-soft <see cref="IDistributedCache"/> helpers
/// (SPEC-20260916-performance-memory-cache RNF-003): a down/unreachable cache
/// backend logs a warning and behaves as a miss — it never fails a request.
/// </summary>
public static class SafeCache
{
    public static async Task<string?> GetStringAsync(
        IDistributedCache cache, string key, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var value = await cache.GetStringAsync(key, ct);
            RecordHit(key, value is not null);
            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache get failed for {Key} — treating as miss: {Message}", key, ex.Message);
            RecordHit(key, false);
            return null;
        }
    }

    public static async Task<byte[]?> GetAsync(
        IDistributedCache cache, string key, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var value = await cache.GetAsync(key, ct);
            RecordHit(key, value is not null);
            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache get failed for {Key} — treating as miss: {Message}", key, ex.Message);
            RecordHit(key, false);
            return null;
        }
    }

    /// <summary>Records hit/miss tagged by bounded cache region (RF-004).</summary>
    private static void RecordHit(string key, bool hit)
    {
        var tag = new KeyValuePair<string, object?>("region", TelemetryTags.RegionFor(key));
        if (hit)
            KnowledgeHubMetrics.CacheHits.Add(1, tag);
        else
            KnowledgeHubMetrics.CacheMisses.Add(1, tag);

        (CacheManagerService.Current as CacheManagerService)?.RecordHit(hit);
    }

    public static async Task SetStringAsync(
        IDistributedCache cache, string key, string value, TimeSpan ttl,
        ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await cache.SetStringAsync(key, value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
            CacheManagerService.Current?.TrackKey(key, System.Text.Encoding.UTF8.GetByteCount(value), ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache set failed for {Key} — skipping: {Message}", key, ex.Message);
        }
    }

    public static async Task SetAsync(
        IDistributedCache cache, string key, byte[] value, TimeSpan ttl,
        ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await cache.SetAsync(key, value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
            CacheManagerService.Current?.TrackKey(key, value.Length, ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache set failed for {Key} — skipping: {Message}", key, ex.Message);
        }
    }

    public static async Task RemoveAsync(
        IDistributedCache cache, string key,
        ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(key, ct);
            CacheManagerService.Current?.RemoveKey(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache remove failed for {Key} — skipping: {Message}", key, ex.Message);
        }
    }

    /// <summary>Serialize + set a JSON payload.</summary>
    public static Task SetJsonAsync<T>(
        IDistributedCache cache, string key, T value, TimeSpan ttl,
        ILogger logger, CancellationToken ct = default) =>
        SetStringAsync(cache, key, JsonSerializer.Serialize(value), ttl, logger, ct);
}
