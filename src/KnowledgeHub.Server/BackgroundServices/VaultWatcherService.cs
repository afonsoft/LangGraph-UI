using System.Collections.Concurrent;
using System.Text.Json;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.BackgroundServices;

/// <summary>
/// Watches active ObsidianVault sources (SPEC-03 RF-005): one FileSystemWatcher per
/// vault, events debounced ~500ms into per-file incremental sync; the watcher set
/// refreshes periodically and honors per-source AutoSync/SyncIntervalMinutes for
/// full re-scans. Failures are logged, never crash the host.
/// </summary>
public sealed class VaultWatcherService(
    IServiceScopeFactory scopeFactory,
    IngestionService ingestion,
    ILogger<VaultWatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, VaultWatch> _watches = new();
    private readonly ConcurrentDictionary<(Guid SourceId, string Path), DateTimeOffset> _pendingFiles = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastFullSync = new();

    private sealed record VaultWatch(string Root, FileSystemWatcher Watcher, SourceType Type);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshWatchersAsync(stoppingToken);
                await FlushPendingFilesAsync(stoppingToken);
                await RunDueAutoSyncsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vault watcher loop failed — retrying");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
        }
    }

    private async Task RefreshWatchersAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();

        var vaults = await db.Sources
            .Where(s => s.IsActive && (s.SourceType == SourceType.ObsidianVault || s.SourceType == SourceType.DocumentFile))
            .ToListAsync(ct);

        var wanted = vaults
            .Select(s => (Source: s, Root: IngestionService.ResolveVaultRoot(s.ConfigurationJson)))
            // DocumentFile roots only get a watcher when 'path' is a directory.
            .Where(x => x.Root is not null && Directory.Exists(x.Root))
            .ToDictionary(x => x.Source.Id, x => (x.Source, x.Root!));

        // drop watchers for deactivated/removed sources
        foreach (var id in _watches.Keys.ToList())
        {
            if (wanted.ContainsKey(id))
                continue;
            if (_watches.TryRemove(id, out var watch))
                watch.Watcher.Dispose();
        }

        foreach (var (id, (source, root)) in wanted)
        {
            if (_watches.ContainsKey(id))
                continue;

            var isVault = source.SourceType == SourceType.ObsidianVault;
            var watcher = new FileSystemWatcher(root, isVault ? "*.md" : "*.*")
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            watcher.Created += (_, e) => Enqueue(id, source.SourceType, root, e);
            watcher.Changed += (_, e) => Enqueue(id, source.SourceType, root, e);
            watcher.Deleted += (_, e) => Enqueue(id, source.SourceType, root, e);
            watcher.Renamed += (_, e) => { Enqueue(id, source.SourceType, root, e); EnqueueOldName(id, source.SourceType, root, e); };
            _watches[id] = new VaultWatch(root, watcher, source.SourceType);
            _lastFullSync.TryAdd(id, DateTimeOffset.MinValue);
            logger.LogInformation("Watching vault '{Name}' at {Root}", source.Name, root);
        }

        await Task.Delay(RefreshInterval, ct);
    }

    private void Enqueue(Guid sourceId, SourceType type, string root, FileSystemEventArgs e)
    {
        var relative = Path.GetRelativePath(root, e.FullPath);
        if (relative.Contains($"{Path.DirectorySeparatorChar}.", StringComparison.Ordinal) || relative.StartsWith('.'))
            return; // .obsidian/ and hidden dirs excluded
        if (type == SourceType.DocumentFile
            && !Ingestion.Connectors.DocumentFileConnector.SupportedExtensions.Contains(Path.GetExtension(e.FullPath)))
            return;
        _pendingFiles[(sourceId, relative)] = DateTimeOffset.UtcNow + Debounce;
    }

    private void EnqueueOldName(Guid sourceId, SourceType type, string root, RenamedEventArgs e)
    {
        var relative = Path.GetRelativePath(root, e.OldFullPath);
        if (type == SourceType.DocumentFile
            && !Ingestion.Connectors.DocumentFileConnector.SupportedExtensions.Contains(Path.GetExtension(e.OldFullPath)))
            return;
        _pendingFiles[(sourceId, relative)] = DateTimeOffset.UtcNow + Debounce;
    }

    private async Task FlushPendingFilesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, due) in _pendingFiles.ToArray())
        {
            if (due > now)
                continue;
            _pendingFiles.TryRemove(key, out _);
            try
            {
                await ingestion.SyncFileAsync(key.SourceId, key.Path, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Incremental sync failed for {Path} (source {SourceId})", key.Path, key.SourceId);
            }
        }
    }

    private async Task RunDueAutoSyncsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var autoSources = await db.Sources
            .Where(s => s.IsActive && s.AutoSyncEnabled
                && (s.SourceType == SourceType.ObsidianVault
                    || s.SourceType == SourceType.WebPage
                    || s.SourceType == SourceType.DocumentFile))
            .Select(s => new { s.Id, s.SyncIntervalMinutes })
            .ToListAsync(ct);

        foreach (var source in autoSources)
        {
            var interval = TimeSpan.FromMinutes(source.SyncIntervalMinutes ?? 30);
            var last = _lastFullSync.GetValueOrDefault(source.Id, DateTimeOffset.MinValue);
            if (DateTimeOffset.UtcNow - last < interval)
                continue;
            _lastFullSync[source.Id] = DateTimeOffset.UtcNow;
            try
            {
                await ingestion.SyncAsync(source.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-sync failed for source {SourceId}", source.Id);
            }
        }
    }

    public override void Dispose()
    {
        foreach (var watch in _watches.Values)
            watch.Watcher.Dispose();
        base.Dispose();
    }
}
