namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Human-in-the-loop gate for mutating tools invoked by the agent loop
/// (SPEC-20260914-hitl-tool-approval RF-001). Status: pending | approved |
/// denied | expired. StateJson holds the suspended agent-loop state so
/// /api/agent/resume can continue from the exact call that was gated.
/// </summary>
public sealed class ToolApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ToolName { get; set; }
    /// <summary>Requested arguments (sensitive fields masked).</summary>
    public required string ArgumentsJson { get; set; }
    /// <summary>ui | agent | thread</summary>
    public required string RequestedBy { get; set; }
    public Guid? ThreadId { get; set; }
    public required string Status { get; set; } = "pending";
    /// <summary>Serialized loop state captured at suspension time.</summary>
    public string? StateJson { get; set; }
    /// <summary>Operator-supplied argument overrides (approvedArgs).</summary>
    public string? ApprovedArgsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    /// <summary>Set once the suspended loop consumed this approval — double resume → 409.</summary>
    public DateTimeOffset? ResumedAt { get; set; }
}
