using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Singleton backing the /api/settings/graph endpoints and the runtime graph
/// gates (SPEC-20260923-graph-settings-ui RF-002). The effective snapshot is
/// loaded lazily and cached; <see cref="Invalidate"/> forces a reload on next
/// access so settings edits take effect without restart.
/// </summary>
public sealed class GraphSettingsService(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    IToolCatalogChangeNotifier catalogNotifier,
    ILogger<GraphSettingsService> logger) : IGraphSettingsService
{
    public const int DefaultMaxChunksPerSync = 200;
    public const int DefaultMaxChunkChars = 2000;
    public const int DefaultMaxResults = 200;

    private readonly object _gate = new();
    private volatile GraphSettingsSnapshot? _snapshot;

    /// <inheritdoc/>
    public GraphSettingsSnapshot GetEffective() => Current();

    /// <inheritdoc/>
    public void Invalidate()
    {
        lock (_gate)
            _snapshot = null;
    }

    /// <summary>Retorna o snapshot atual, carregando do store na primeira vez após invalidação.</summary>
    private GraphSettingsSnapshot Current()
    {
        var snap = _snapshot;
        if (snap is not null)
            return snap;
        lock (_gate)
        {
            snap ??= LoadSnapshotAsync().GetAwaiter().GetResult();
            _snapshot = snap;
            return snap;
        }
    }

    /// <summary>Monta o snapshot efetivo: linha persistida → chaves Graph:* → defaults.</summary>
    private async Task<GraphSettingsSnapshot> LoadSnapshotAsync()
    {
        var row = await FindRowAsync(CancellationToken.None);
        if (row is not null)
            return new GraphSettingsSnapshot(
                row.Enabled, row.MaxChunksPerSync, row.MaxChunkChars, row.MaxResults, "store");

        return new GraphSettingsSnapshot(
            configuration.GetValue("Graph:Enabled", true),
            configuration.GetValue("Graph:MaxChunksPerSync", DefaultMaxChunksPerSync),
            configuration.GetValue("Graph:MaxChunkChars", DefaultMaxChunkChars),
            configuration.GetValue("Graph:MaxResults", DefaultMaxResults),
            "env");
    }

    /// <inheritdoc/>
    public async Task<GraphSettingsDto> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var row = await FindRowAsync(cancellationToken);
        var envConfigured = configuration["Graph:Enabled"] is not null
            || configuration["Graph:MaxChunksPerSync"] is not null
            || configuration["Graph:MaxChunkChars"] is not null
            || configuration["Graph:MaxResults"] is not null;
        var effective = GetEffective();

        return new GraphSettingsDto
        {
            Enabled = effective.Enabled,
            MaxChunksPerSync = effective.MaxChunksPerSync,
            MaxChunkChars = effective.MaxChunkChars,
            MaxResults = effective.MaxResults,
            Source = effective.Source,
            EnvConfigured = envConfigured,
            UpdatedAt = row?.UpdatedAt
        };
    }

    /// <inheritdoc/>
    public async Task SaveAsync(SaveGraphSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var row = await db.GraphSettings.SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new GraphSettings { Id = 1 };
            db.GraphSettings.Add(row);
        }
        row.Enabled = request.Enabled;
        row.MaxChunksPerSync = request.MaxChunksPerSync;
        row.MaxChunkChars = request.MaxChunkChars;
        row.MaxResults = request.MaxResults;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        Invalidate();
        // Enabled gates the tools catalog — surface the change to MCP clients.
        await catalogNotifier.NotifyToolsChangedAsync(cancellationToken);
        logger.LogInformation("graph settings saved (enabled {Enabled}, budget {MaxChunksPerSync}, chars {MaxChunkChars}, results {MaxResults})",
            request.Enabled, request.MaxChunksPerSync, request.MaxChunkChars, request.MaxResults);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        await db.GraphSettings.ExecuteDeleteAsync(cancellationToken);
        Invalidate();
        await catalogNotifier.NotifyToolsChangedAsync(cancellationToken);
        logger.LogInformation("graph settings cleared — falling back to Graph:* config/defaults");
    }

    /// <summary>Lê a linha única de GraphSettings em um scope EF próprio (sem tracking).</summary>
    private async Task<GraphSettings?> FindRowAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        return await db.GraphSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }
}
