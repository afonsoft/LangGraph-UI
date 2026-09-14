using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>POST /api/agent — agentic tool-calling loop (SPEC-20260914-agent-chat-loop RF-002).</summary>
public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent");

        group.MapPost("/", async (AgentRequest request, IAgentService agent, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt) && request.Messages is not { Count: > 0 })
                return Results.BadRequest(new { error = "prompt or messages[] is required" });
            if (!agent.IsConfigured)
                return Results.BadRequest(new { error = "agent requires a chat provider (Chat:Provider)" });

            return Results.Ok(await agent.RunAsync(request, ct));
        });

        return group;
    }
}
