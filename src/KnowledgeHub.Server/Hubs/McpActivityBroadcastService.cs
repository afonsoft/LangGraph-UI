using KnowledgeHub.McpEngine.Activity;
using Microsoft.AspNetCore.SignalR;

namespace KnowledgeHub.Server.Hubs;

/// <summary>
/// Forwards <see cref="IMcpActivityFeed"/> events to connected Monitor clients.
/// Session lifecycle events use dedicated messages; requests/tool calls go
/// through the generic "Activity" channel (SPEC-05 §5).
/// </summary>
public sealed class McpActivityBroadcastService(
    IMcpActivityFeed feed,
    IHubContext<McpMonitorHub> hub,
    ILogger<McpActivityBroadcastService> logger) : IHostedService, IDisposable
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

    private void OnPublished(McpActivityEvent e)
    {
        _ = BroadcastAsync(e);
    }

    private async Task BroadcastAsync(McpActivityEvent e)
    {
        try
        {
            switch (e.Kind)
            {
                case McpActivityKind.SessionOpened:
                    await hub.Clients.All.SendAsync("SessionOpened",
                        new { sessionId = e.SessionId, connectedAt = e.Timestamp });
                    break;
                case McpActivityKind.SessionClosed:
                    await hub.Clients.All.SendAsync("SessionClosed",
                        new { sessionId = e.SessionId });
                    break;
                default:
                    // SPEC-20260915-mcp-monitor-activity RF-002: shared mapper —
                    // identical wire shape to the hub snapshot.
                    await hub.Clients.All.SendAsync("Activity", McpMonitorEventMapper.Map(e));
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to broadcast MCP activity to monitor clients");
        }
    }

    public void Dispose() => feed.Published -= OnPublished;
}
