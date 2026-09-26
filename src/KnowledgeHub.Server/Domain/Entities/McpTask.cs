namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// SPEC-20260926-mcp-sdk-alignment RF-004: durable row behind
/// <c>IMcpTaskStore</c> — MCP Tasks extension handles (taskId) survive restarts
/// and stateless requests. Result/Error/InputRequests store the serialized
/// protocol payloads.
/// </summary>
public sealed class McpTask
{
    /// <summary>Opaque task handle issued to clients (<c>mt_*</c>).</summary>
    public required string TaskId { get; set; }
    /// <summary>working | input_required | completed | cancelled | failed.</summary>
    public required string Status { get; set; }
    public string? StatusMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Suggested client poll interval, ms.</summary>
    public long? PollIntervalMs { get; set; }
    /// <summary>Row is ignored past <c>CreatedAt + Ttl</c>.</summary>
    public long? TtlMs { get; set; }
    /// <summary>Serialized final <c>result</c> when Status=completed.</summary>
    public string? ResultJson { get; set; }
    /// <summary>Serialized JSON-RPC <c>error</c> when Status=failed.</summary>
    public string? ErrorJson { get; set; }
    /// <summary>Serialized inputRequests map when Status=input_required.</summary>
    public string? InputRequestsJson { get; set; }
}
