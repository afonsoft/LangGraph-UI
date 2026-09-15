using KnowledgeHub.McpEngine.Activity;
using Microsoft.AspNetCore.SignalR;

namespace KnowledgeHub.Server.Hubs;

/// <summary>
/// SignalR hub feeding the MCP Monitor page (SPEC-05 RF-003).
/// On connect the client receives the buffered snapshot; afterwards
/// <see cref="McpActivityBroadcastService"/> pushes live events.
/// </summary>
public sealed class McpMonitorHub(IMcpActivityFeed feed) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // SPEC-20260915-mcp-monitor-activity RF-002: snapshot uses the same wire
        // DTO as the live broadcast so the client replays it with one code path.
        await Clients.Caller.SendAsync("Snapshot",
            feed.Snapshot().Select(McpMonitorEventMapper.Map).ToList());
        await base.OnConnectedAsync();
    }
}
