using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>Agentic tool-calling loop (SPEC-20260914-agent-chat-loop RF-001).</summary>
public interface IAgentService
{
    /// <summary>True when a chat provider is configured (Chat:Provider != none).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs the model→tools→model loop until a final answer or a hard cap
    /// (<c>Agent:MaxIterations</c>/<c>Agent:MaxToolCalls</c>) is reached.
    /// </summary>
    Task<AgentResponse> RunAsync(AgentRequest request, CancellationToken cancellationToken = default);
}
