using System.Text.Json;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Shared.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// REST façade over the live MCP tool catalog (SPEC-20260914-playground-tools
/// RF-002/RF-003): same IDynamicToolCatalog + handlers as tools/list/tools/call.
/// </summary>
public static class ToolsEndpoints
{
    public static RouteGroupBuilder MapToolsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tools");

        group.MapGet("/", async (IDynamicToolCatalog catalog, HttpContext http, CancellationToken ct) =>
        {
            var tools = await catalog.GetToolsAsync(http.RequestServices, ct);
            return Results.Ok(new ToolListResponse
            {
                Tools = tools.Select(t => new ToolDescriptorDto
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = JsonSerializer.SerializeToElement(t.InputSchema),
                    ReadOnly = t.ReadOnly
                }).ToList()
            });
        });

        group.MapPost("/{name}", async (
            IDynamicToolCatalog catalog, HttpContext http, string name, CancellationToken ct) =>
        {
            var tool = (await catalog.GetToolsAsync(http.RequestServices, ct))
                .FirstOrDefault(t => t.Name == name);
            if (tool is null)
                return Results.NotFound(new { error = $"unknown tool '{name}'" });

            var arguments = await ReadArgumentsAsync(http, ct);
            var context = new ToolCallContext
            {
                Services = http.RequestServices,
                Arguments = arguments
            };

            try
            {
                var result = await tool.Handler(context, ct);
                return Results.Ok(result);
            }
            catch (McpProtocolException ex)
            {
                // MCP semantics: argument/validation errors are isError results,
                // not HTTP 500 — the Playground renders them in the result pane.
                return Results.Ok(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = ex.Message }],
                    IsError = true
                });
            }
        });

        return group;
    }

    private static async Task<Dictionary<string, JsonElement>?> ReadArgumentsAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is null or 0)
            return null;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(http.Request.Body, cancellationToken: ct);
        if (body.ValueKind is not JsonValueKind.Object)
            return null;
        return body.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
