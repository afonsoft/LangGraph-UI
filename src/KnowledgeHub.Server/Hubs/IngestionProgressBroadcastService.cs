using KnowledgeHub.Server.Ingestion;
using Microsoft.AspNetCore.SignalR;

namespace KnowledgeHub.Server.Hubs;

/// <summary>
/// Forwards <see cref="IIngestionProgressFeed"/> events to connected clients as
/// "IngestionProgress" messages on the existing hub
/// (SPEC-20260925-job-progress-feed RF-001).
/// </summary>
public sealed class IngestionProgressBroadcastService(
    IIngestionProgressFeed feed,
    IHubContext<McpMonitorHub> hub,
    ILogger<IngestionProgressBroadcastService> logger) : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        feed.Published += OnPublished;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        feed.Published -= OnPublished;
        return Task.CompletedTask;
    }

    private void OnPublished(IngestionProgressEvent e) => _ = BroadcastAsync(e);

    private async Task BroadcastAsync(IngestionProgressEvent e)
    {
        try
        {
            await hub.Clients.All.SendAsync("IngestionProgress", new
            {
                jobId = e.JobId,
                sourceId = e.SourceId,
                status = e.Status,
                processed = e.Processed,
                skipped = e.Skipped,
                failed = e.Failed,
                chunksCreated = e.ChunksCreated
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to broadcast ingestion progress");
        }
    }

    public void Dispose() => feed.Published -= OnPublished;
}
