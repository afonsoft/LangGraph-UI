using KnowledgeHub.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Runtime per-integration enable flag — persisted in <c>IntegrationStates</c>
/// (works keyless, unlike secrets) and cached in memory. Upstream tool
/// providers resolve this at catalog-build time; a toggle bumps
/// <see cref="Mcp.IToolCatalogChangeNotifier"/> so the aggregate rebuilds and
/// the provider's tools vanish from <c>tools/list</c> — a stale
/// <c>tools/call</c> then hits the standard unknown-tool error.
/// </summary>
public interface IIntegrationStateService
{
    /// <summary>True when the provider is usable (default — no row means enabled).</summary>
    Task<bool> IsEnabledAsync(string provider, CancellationToken ct = default);

    /// <summary>Persists the flag; callers should also reset the provider's
    /// upstream session and bump the tool-catalog notifier.</summary>
    Task SetEnabledAsync(string provider, bool enabled, CancellationToken ct = default);
}

public sealed class IntegrationStateService(
    IServiceScopeFactory scopeFactory) : IIntegrationStateService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, bool>? _cache;

    public async Task<bool> IsEnabledAsync(string provider, CancellationToken ct = default)
    {
        var cache = _cache ??= await LoadAsync(ct);
        return !cache.TryGetValue(provider, out var enabled) || enabled;
    }

    public async Task SetEnabledAsync(string provider, bool enabled, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var row = await db.IntegrationStates.SingleOrDefaultAsync(s => s.Provider == provider, ct);
        if (row is null)
            db.IntegrationStates.Add(new Domain.Entities.IntegrationState { Provider = provider, IsEnabled = enabled });
        else
        {
            row.IsEnabled = enabled;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        await _gate.WaitAsync(ct);
        try { _cache = null; }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, bool>> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is not null)
                return _cache;
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            _cache = await db.IntegrationStates.AsNoTracking()
                .ToDictionaryAsync(s => s.Provider, s => s.IsEnabled, ct);
            return _cache;
        }
        finally { _gate.Release(); }
    }
}
