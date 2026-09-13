namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// In-memory activity feed for the MCP Monitor UI (SPEC-01 RF-003).
/// Ring buffer of recent events plus a pub/sub hook for live subscribers (SignalR).
/// Implementations must never throw into the MCP pipeline.
/// </summary>
public interface IMcpActivityFeed
{
    /// <summary>Maximum number of retained events.</summary>
    int Capacity { get; }

    /// <summary>Append an event; evicts the oldest when at capacity.</summary>
    void Record(McpActivityEvent activityEvent);

    /// <summary>Current buffered events, oldest first.</summary>
    IReadOnlyList<McpActivityEvent> Snapshot();

    /// <summary>Raised for each recorded event, after buffering.</summary>
    event Action<McpActivityEvent>? Published;
}
