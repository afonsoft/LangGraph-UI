using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Caching;

/// <summary>
/// Cache management and inspection service (SPEC-20260924-redis-cache-and-tool-caching).
/// </summary>
public interface ICacheManagerService
{
    Task<CacheStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<ClearCacheResultDto> ClearAllAsync(CancellationToken ct = default);
    void TrackKey(string key, long sizeBytes, TimeSpan? ttl = null);
    void RemoveKey(string key);
}
