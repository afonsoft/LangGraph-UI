using KnowledgeHub.Client.Services;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Tests.Unit.Client;

// Covers SPEC-20260915-mcp-monitor-activity RF-003/RF-005: snapshot replay and
// live events share this routine — session lifecycle updates the session map,
// everything else appends to the bounded activity list.
public class McpMonitorReplayTests
{
    private static McpMonitorEventDto Event(
        string kind, string? sessionId = null, string? method = null) => new()
        {
            Timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            Kind = kind,
            SessionId = sessionId,
            Method = method
        };

    [Fact]
    public void SessionOpened_AddsActiveSession()
    {
        var sessions = new Dictionary<string, McpMonitorReplay.SessionInfo>();
        var activity = new List<McpMonitorEventDto>();

        McpMonitorReplay.Apply(Event(McpMonitorEventKind.SessionOpened, "s1"), sessions, activity, 200);

        Assert.True(sessions.ContainsKey("s1"));
        Assert.Empty(activity);
    }

    [Fact]
    public void SessionOpenedThenClosed_LeavesNoActiveSession()
    {
        var sessions = new Dictionary<string, McpMonitorReplay.SessionInfo>();
        var activity = new List<McpMonitorEventDto>();

        McpMonitorReplay.Apply(Event(McpMonitorEventKind.SessionOpened, "s1"), sessions, activity, 200);
        McpMonitorReplay.Apply(Event(McpMonitorEventKind.SessionClosed, "s1"), sessions, activity, 200);

        Assert.Empty(sessions);
        Assert.Empty(activity);
    }

    [Fact]
    public void ActivityKinds_AppendToList()
    {
        var sessions = new Dictionary<string, McpMonitorReplay.SessionInfo>();
        var activity = new List<McpMonitorEventDto>();

        McpMonitorReplay.Apply(Event(McpMonitorEventKind.Request, "s1", "initialize"), sessions, activity, 200);
        McpMonitorReplay.Apply(Event(McpMonitorEventKind.ToolCall, "s1", "tools/call:search"), sessions, activity, 200);

        Assert.Equal(2, activity.Count);
        Assert.Empty(sessions);
    }

    [Fact]
    public void NullSessionId_SkipsSessionBookkeeping()
    {
        var sessions = new Dictionary<string, McpMonitorReplay.SessionInfo>();
        var activity = new List<McpMonitorEventDto>();

        McpMonitorReplay.Apply(Event(McpMonitorEventKind.SessionOpened, sessionId: null), sessions, activity, 200);

        Assert.Empty(sessions);
    }

    [Fact]
    public void ActivityList_IsCappedAtMax()
    {
        var sessions = new Dictionary<string, McpMonitorReplay.SessionInfo>();
        var activity = new List<McpMonitorEventDto>();

        for (var i = 0; i < 5; i++)
            McpMonitorReplay.Apply(Event(McpMonitorEventKind.Request, method: $"m{i}"), sessions, activity, 3);

        Assert.Equal(3, activity.Count);
        Assert.Equal("m4", activity[^1].Method);
    }
}
