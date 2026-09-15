using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Client.Services;

/// <summary>
/// Applies monitor events — snapshot replay or live — to the page state
/// (SPEC-20260915-mcp-monitor-activity RF-003/RF-005): session lifecycle entries
/// maintain the active-session map, everything else appends to the bounded
/// activity list.
/// </summary>
public static class McpMonitorReplay
{
    public static void Apply(
        McpMonitorEventDto e,
        IDictionary<string, DateTimeOffset> sessions,
        IList<McpMonitorEventDto> activity,
        int maxActivity)
    {
        switch (e.Kind)
        {
            case McpMonitorEventKind.SessionOpened:
                if (e.SessionId is not null)
                    sessions[e.SessionId] = e.Timestamp;
                break;
            case McpMonitorEventKind.SessionClosed:
                if (e.SessionId is not null)
                    sessions.Remove(e.SessionId);
                break;
            default:
                activity.Add(e);
                while (activity.Count > maxActivity)
                    activity.RemoveAt(0);
                break;
        }
    }
}
