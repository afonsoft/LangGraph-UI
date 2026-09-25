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

    /// <summary>SPEC-20260926-settings-ux-embeddings RF-003: removes ONE cache
    /// entry — both the real <c>IDistributedCache</c> value (L1+L2) and the
    /// tracked-key record. Returns whether the key was tracked.</summary>
    Task<bool> RemoveEntryAsync(string key, CancellationToken ct = default);
}
