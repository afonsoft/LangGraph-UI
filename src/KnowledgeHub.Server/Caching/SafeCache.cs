using System.Text.Json;
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
            return await cache.GetStringAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache get failed for {Key} — treating as miss: {Message}", key, ex.Message);
            return null;
        }
    }

    public static async Task<byte[]?> GetAsync(
        IDistributedCache cache, string key, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            return await cache.GetAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache get failed for {Key} — treating as miss: {Message}", key, ex.Message);
            return null;
        }
    }

    public static async Task SetStringAsync(
        IDistributedCache cache, string key, string value, TimeSpan ttl,
        ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await cache.SetStringAsync(key, value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("cache set failed for {Key} — skipping: {Message}", key, ex.Message);
        }
    }

    /// <summary>Serialize + set a JSON payload.</summary>
    public static Task SetJsonAsync<T>(
        IDistributedCache cache, string key, T value, TimeSpan ttl,
        ILogger logger, CancellationToken ct = default) =>
        SetStringAsync(cache, key, JsonSerializer.Serialize(value), ttl, logger, ct);
}
