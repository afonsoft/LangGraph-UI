using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Ingestion.Staging;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.BackgroundServices;

/// <summary>
/// SPEC-20260924-hosted-services-and-serilog-logging RF-003: spaced-out
/// maintenance — purges staging directories that no longer belong to a known
/// source (deleted sources normally self-clean, so these are leftovers).
/// Runs on a long interval and never interferes with active ingestion.
/// </summary>
public sealed class MaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MaintenanceBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(
            Math.Max(1, configuration.GetValue("Maintenance:IntervalHours", 6)));

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PurgeOrphanedStagingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance pass failed — retrying next cycle");
            }
        }
    }

    private async Task PurgeOrphanedStagingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var staging = scope.ServiceProvider.GetRequiredService<IStagingStorageService>();

        var knownIds = (await db.Sources.AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync(ct)).ToHashSet();

        var removed = await staging.CleanupOrphanedStagingAsync(knownIds, ct);
        if (removed > 0)
            logger.LogInformation("Maintenance purged {Count} orphaned staging directorie(s)", removed);
    }
}
