namespace KnowledgeHub.Server.Api;

/// <summary>
/// SPEC-20260926-ops-and-ui-polish: anonymous MCP capabilities probe — the
/// login page advertises the legacy SSE transport only when it is actually
/// served (disabled under <c>Mcp:SessionMode=Stateless</c>).
/// </summary>
public static class McpInfoEndpoints
{
    public static RouteGroupBuilder MapMcpInfoApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mcp").WithTags("MCP");

        group.MapGet("/capabilities", (IConfiguration cfg) =>
        {
            var mode = cfg["Mcp:SessionMode"] ?? "StatefulForInitializeClients";
            var legacySse = !mode.Equals("Stateless", StringComparison.OrdinalIgnoreCase);
            return Results.Ok(new { sessionMode = mode, legacySse });
        }).AllowAnonymous();

        return group;
    }
}
