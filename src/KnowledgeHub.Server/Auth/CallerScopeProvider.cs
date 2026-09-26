using KnowledgeHub.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KnowledgeHub.Server.Auth;

/// <summary>Resolves the caller's <see cref="CallerScope"/> once per request.</summary>
public interface ICallerScopeProvider
{
    Task<CallerScope> GetAsync(CancellationToken ct);
}

/// <summary>
/// SPEC-20260923-source-authorization RF-002: apikey principals get their
/// scope from the key row, cached 60s in <see cref="IMemoryCache"/> (the PUT
/// scopes endpoint evicts on write). Cookie sessions and anonymous callers
/// resolve to <see cref="CallerScope.Unrestricted"/>.
/// </summary>
public sealed class CallerScopeProvider(
    IHttpContextAccessor http,
    IMemoryCache memory,
    KnowledgeHubDbContext db) : ICallerScopeProvider
{
    internal static string CacheKey(Guid keyId) => $"keyscope:{keyId}";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private CallerScope? _resolved;

    public async Task<CallerScope> GetAsync(CancellationToken ct)
    {
        if (_resolved is not null)
            return _resolved;

        var user = http.HttpContext?.User;
        if (user?.FindFirst(ApiKeyAuthenticationHandler.AuthMethodClaim)?.Value != "apikey"
            || !Guid.TryParse(user.FindFirst(ApiKeyAuthenticationHandler.KeyIdClaim)?.Value, out var keyId))
            return _resolved = CallerScope.Unrestricted;

        var scope = await memory.GetOrCreateAsync(CacheKey(keyId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            var row = await db.ApiKeys.AsNoTracking()
                .Where(k => k.Id == keyId)
                .Select(k => new { k.AllowedSourceIdsJson, k.AllowedToolsJson, k.AllowWrite })
                .FirstOrDefaultAsync(ct);
            return CallerScope.FromJson(keyId, row?.AllowedSourceIdsJson, row?.AllowedToolsJson, row?.AllowWrite ?? true);
        });
        return _resolved = scope ?? CallerScope.Unrestricted;
    }
}
