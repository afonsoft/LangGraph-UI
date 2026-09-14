using System.Text.Json;

namespace KnowledgeHub.Shared.Contracts;

/// <summary>ToolApproval as exposed by /api/approvals (SPEC-20260914-hitl-tool-approval).</summary>
public sealed record ApprovalDto
{
    public required Guid Id { get; init; }
    public required string ToolName { get; init; }
    public required string ArgumentsJson { get; init; }
    public required string RequestedBy { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public DateTimeOffset? ResumedAt { get; init; }
}

/// <summary>POST /api/approvals/{id}/approve body — optional arg overrides.</summary>
public sealed record ApproveApprovalRequest
{
    /// <summary>Replacement arguments; when present the resumed call uses these instead of the originals.</summary>
    public JsonElement? ApprovedArgs { get; init; }
}

/// <summary>POST /api/agent/resume body.</summary>
public sealed record ResumeAgentRequest
{
    public required Guid ApprovalId { get; init; }
}
