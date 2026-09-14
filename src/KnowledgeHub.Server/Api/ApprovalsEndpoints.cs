using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// /api/approvals — HITL gate for mutating tools invoked by the agent loop
/// (SPEC-20260914-hitl-tool-approval RF-002/RF-003).
/// </summary>
public static class ApprovalsEndpoints
{
    public static RouteGroupBuilder MapApprovalsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approvals");

        group.MapGet("/", async (IApprovalService approvals, string? status, CancellationToken ct) =>
            Results.Ok(await approvals.ListAsync(status, ct)));

        group.MapPost("/{id:guid}/approve", async (
            Guid id, ApproveApprovalRequest? body, IApprovalService approvals, CancellationToken ct) =>
            Results.Ok(await approvals.ApproveAsync(id, body?.ApprovedArgs, ct)));

        // Deny resolves the gate AND continues the loop with a "denied" tool result,
        // so the model answers without mutating anything (AC: deny → responde sem escrever).
        group.MapPost("/{id:guid}/deny", async (
            Guid id, IApprovalService approvals, IAgentService agent, CancellationToken ct) =>
        {
            await approvals.DenyAsync(id, ct);
            return Results.Ok(await agent.ResumeAsync(id, allowDenied: true, ct));
        });

        return group;
    }
}
