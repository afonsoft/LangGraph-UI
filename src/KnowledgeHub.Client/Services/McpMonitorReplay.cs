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
    /// <summary>Session row state — connect time + authenticated caller.</summary>
    public sealed record SessionInfo(DateTimeOffset ConnectedAt, string? Caller);

    public static void Apply(
        McpMonitorEventDto e,
        IDictionary<string, SessionInfo> sessions,
        IList<McpMonitorEventDto> activity,
        int maxActivity)
    {
        switch (e.Kind)
        {
            case McpMonitorEventKind.SessionOpened:
                if (e.SessionId is not null)
                    sessions[e.SessionId] = new SessionInfo(e.Timestamp, e.Caller);
                AppendActivity(e);
                break;
            case McpMonitorEventKind.SessionClosed:
                if (e.SessionId is not null)
                    sessions.Remove(e.SessionId);
                AppendActivity(e);
                break;
            default:
                AppendActivity(e);
                break;
        }

        // SPEC-20260926-ops-and-ui-polish: session transitions belong to the
        // activity list too — the "sessões" kind filter previously matched
        // nothing because transitions only updated the sessions map.
        void AppendActivity(McpMonitorEventDto ev)
        {
            activity.Add(ev);
            while (activity.Count > maxActivity)
                activity.RemoveAt(0);
        }
    }
}
