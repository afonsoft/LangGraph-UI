namespace KnowledgeHub.McpEngine.Activity;

/// <summary>Kind of MCP activity event recorded in the feed (SPEC-01 RF-003).</summary>
public enum McpActivityKind
{
    SessionOpened,
    SessionClosed,
    Request,
    ToolCall,
    ApprovalRequested,
    ApprovalResolved
}

/// <summary>One observable MCP activity entry for the Monitor UI.</summary>
public sealed record McpActivityEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required McpActivityKind Kind { get; init; }
    public string? SessionId { get; init; }
    public string? Transport { get; init; }
    public string? Method { get; init; }
    public string? ToolName { get; init; }
    public double? DurationMs { get; init; }
    public bool? Succeeded { get; init; }
    public string? Error { get; init; }
    /// <summary>Authenticated caller label for audit (user + auth method +
    /// API-key fragment) — resolved from the ambient HTTP context.</summary>
    public string? Caller { get; init; }
}
