using KnowledgeHub.McpEngine.Activity;
using Microsoft.AspNetCore.Builder;

namespace KnowledgeHub.McpEngine;

/// <summary>Endpoint wiring for the KnowledgeHub MCP server (SPEC-01 RF-001).</summary>
public static class McpEndpointExtensions
{
    /// <summary>
    /// Installs session-observation middleware around <c>/mcp</c> and maps the SDK
    /// endpoints: Streamable HTTP on <c>/mcp</c>, legacy SSE on <c>/mcp/sse</c> and
    /// <c>/mcp/message</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapKnowledgeHubMcp(this WebApplication app)
    {
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
            branch => branch.UseMiddleware<McpSessionMiddleware>());

        return app.MapMcp("/mcp");
    }
}
