using System.Text.Json;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>Approval workflow for gated tool calls (SPEC-20260914-hitl-tool-approval).</summary>
public interface IApprovalService
{
    /// <summary>Lists approvals; pending entries past the timeout are lazily expired.</summary>
    Task<IReadOnlyList<ApprovalDto>> ListAsync(string? status, CancellationToken ct = default);

    /// <summary>Marks pending → approved (optional arg overrides). 409 on non-pending; 404 unknown.</summary>
    Task<ApprovalDto> ApproveAsync(Guid id, JsonElement? approvedArgs, CancellationToken ct = default);

    /// <summary>Marks pending → denied. 409 on non-pending; 404 unknown.</summary>
    Task<ApprovalDto> DenyAsync(Guid id, CancellationToken ct = default);
}
