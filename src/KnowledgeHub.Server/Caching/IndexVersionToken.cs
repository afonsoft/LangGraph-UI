using Microsoft.Extensions.Caching.Distributed;

namespace KnowledgeHub.Server.Caching;

/// <summary>
/// Shared index-version token (SPEC-20260916-performance-memory-cache RF-005;
/// reused by SPEC-20260923-agent-runtime-hardening RF-003): ingestion bumps the
/// token so every result/answer key embedding it invalidates on sync. A
/// missing/unreadable token gets a fresh value — all existing keys miss, which
/// is correct.
/// </summary>
public static class IndexVersionToken
{
    public static async Task<string> GetAsync(
        IDistributedCache cache, ILogger logger, CancellationToken ct)
    {
        var version = await SafeCache.GetStringAsync(cache, CacheKeys.IndexVersion, logger, ct);
        if (version is not null)
            return version;
        version = Guid.NewGuid().ToString("N");
        await SafeCache.SetStringAsync(cache, CacheKeys.IndexVersion, version,
            null, logger, ct);
        return version;
    }
}
