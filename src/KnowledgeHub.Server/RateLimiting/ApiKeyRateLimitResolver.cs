using System.Collections.Concurrent;
using KnowledgeHub.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.RateLimiting;

/// <summary>Per-key rate-limit override — every field nullable; a null field
/// falls back to the global <c>RateLimiting:*</c> value
/// (SPEC-20260923-per-key-rate-limits RF-002).</summary>
public sealed record ApiKeyRateLimitOverride(
    int? LlmPermits, int? LlmWindowSeconds, int? SyncPermits, int? SyncWindowSeconds);

/// <summary>Sync lookup of per-key overrides — rate-limit partition callbacks
/// cannot await, so overrides live in a cache invalidated on admin writes.</summary>
public interface IApiKeyRateLimitResolver
{
    /// <summary>True when <paramref name="keyId"/> carries any override field.</summary>
    bool TryGetOverride(Guid keyId, out ApiKeyRateLimitOverride? value);
    /// <summary>Drops the cache; next lookup reloads from the store.</summary>
    void Invalidate();
}

/// <summary>
/// Singleton resolver: lazily loads only ApiKey rows that have at least one
/// override column set, keyed by key id. Admin mutations call
/// <see cref="Invalidate"/> so changes apply to new partitions immediately.
/// </summary>
public sealed class ApiKeyRateLimitResolver(IServiceScopeFactory scopeFactory) : IApiKeyRateLimitResolver
{
    private readonly object _gate = new();
    private volatile ConcurrentDictionary<Guid, ApiKeyRateLimitOverride>? _cache;

    /// <inheritdoc/>
    public bool TryGetOverride(Guid keyId, out ApiKeyRateLimitOverride? value) =>
        Current().TryGetValue(keyId, out value);

    /// <inheritdoc/>
    public void Invalidate()
    {
        lock (_gate)
            _cache = null;
    }

    /// <summary>Retorna o mapa atual, carregando do banco na primeira vez após invalidação.</summary>
    private ConcurrentDictionary<Guid, ApiKeyRateLimitOverride> Current()
    {
        var cache = _cache;
        if (cache is not null)
            return cache;
        lock (_gate)
        {
            cache ??= Load();
            _cache = cache;
            return cache;
        }
    }

    /// <summary>Carrega só as keys com algum override — o join é raro e a tabela pequena.</summary>
    private ConcurrentDictionary<Guid, ApiKeyRateLimitOverride> Load()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var rows = db.ApiKeys.AsNoTracking()
            .Where(k => k.LlmRateLimitPermits != null
                || k.LlmRateLimitWindowSeconds != null
                || k.SyncRateLimitPermits != null
                || k.SyncRateLimitWindowSeconds != null)
            .Select(k => new
            {
                k.Id,
                k.LlmRateLimitPermits,
                k.LlmRateLimitWindowSeconds,
                k.SyncRateLimitPermits,
                k.SyncRateLimitWindowSeconds
            })
            .ToList();
        return new ConcurrentDictionary<Guid, ApiKeyRateLimitOverride>(
            rows.Select(r => KeyValuePair.Create(r.Id,
                new ApiKeyRateLimitOverride(
                    r.LlmRateLimitPermits, r.LlmRateLimitWindowSeconds,
                    r.SyncRateLimitPermits, r.SyncRateLimitWindowSeconds))));
    }
}
