using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>Agentic tool-calling loop (SPEC-20260914-agent-chat-loop).</summary>
public interface IAgentService
{
    bool IsConfigured { get; }

    /// <summary>Runs the model→tools→model loop; may suspend with AwaitingApprovalId.</summary>
    Task<AgentResponse> RunAsync(AgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues a suspended run: executes the approved call (approvedArgs override
    /// wins), or — when <paramref name="allowDenied"/> — injects a "denied" tool
    /// result so the model can answer without mutating anything.
    /// </summary>
    Task<AgentResponse> ResumeAsync(Guid approvalId, bool allowDenied = false, CancellationToken cancellationToken = default);
}
