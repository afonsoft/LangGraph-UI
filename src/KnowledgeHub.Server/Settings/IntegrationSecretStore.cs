using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Singleton store backed by <see cref="KnowledgeHubDbContext"/> (via scope
/// factory) with values protected by an <see cref="IDataProtector"/> scoped to
/// the "integration-secrets" purpose (SPEC-20260916-firecrawl-mcp-proxy RF-004).
/// Decrypted values are cached in-process for 60s
/// (SPEC-20260916-performance-memory-cache RF-003) — upstream clients resolve
/// the key on every call; the cache skips a scope + query + decrypt per call.
/// Secrets stay in-process only — never the distributed cache.
/// </summary>
public sealed class IntegrationSecretStore(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtection,
    IMemoryCache cache,
    ILogger<IntegrationSecretStore> logger) : IIntegrationSecretStore
{
    private const string Purpose = "integration-secrets";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private IDataProtector Protector => dataProtection.CreateProtector(Purpose);

    private static string CacheKey(string provider) => $"secret:{provider}";

    public async Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default)
    {
        var key = CacheKey(provider);
        if (cache.TryGetValue(key, out string? cached))
            return cached;

        try
        {
            var row = await FindAsync(provider, cancellationToken);
            var value = row is null ? null : Protector.Unprotect(row.ProtectedValue);
            // Negative results are cached too — a missing row would otherwise
            // re-query the DB on every upstream call.
            cache.Set(key, value, CacheTtl);
            return value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "integration secret read failed for provider {Provider} — falling back to env/config", provider);
            return null;
        }
    }

    public async Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(provider, cancellationToken);
        return row is null ? null : new IntegrationSecretInfo(row.Provider, row.KeyHint, row.UpdatedAt);
    }

    public async Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var row = await db.IntegrationSecrets
            .SingleOrDefaultAsync(s => s.Provider == provider, cancellationToken);

        if (row is null)
        {
            row = new IntegrationSecret
            {
                Provider = provider,
                ProtectedValue = "",
                KeyHint = ""
            };
            db.IntegrationSecrets.Add(row);
        }

        row.ProtectedValue = Protector.Protect(secret);
        row.KeyHint = secret.Length >= 4 ? secret[^4..] : secret;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(provider));
    }

    public async Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var removed = await db.IntegrationSecrets
            .Where(s => s.Provider == provider)
            .ExecuteDeleteAsync(cancellationToken);
        cache.Remove(CacheKey(provider));
        return removed > 0;
    }

    private async Task<IntegrationSecret?> FindAsync(string provider, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        return await db.IntegrationSecrets
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Provider == provider, cancellationToken);
    }
}
