using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Single projection from <see cref="McpActivityEvent"/> to the monitor wire DTO
/// (SPEC-20260915-mcp-monitor-activity RF-002) — shared by the hub snapshot and
/// the live broadcast so their payloads cannot diverge again.
/// </summary>
public static class McpMonitorEventMapper
{
    public static McpMonitorEventDto Map(McpActivityEvent e) => new()
    {
        Timestamp = e.Timestamp,
        Kind = e.Kind.ToString(),
        SessionId = e.SessionId,
        Method = e.ToolName is null ? e.Method : $"{e.Method}:{e.ToolName}",
        Detail = e.Error ?? e.Transport,
        DurationMs = e.DurationMs,
        Succeeded = e.Succeeded,
        Caller = e.Caller
    };
}
