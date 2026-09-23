using System.Text.Json;
using KnowledgeHub.Server.Auth;
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
            {
                // SPEC-20260923-source-authorization RF-004: scope-hidden tools
                // get the same friendly isError as MCP tools/call; unknown
                // names keep their 404.
                var exists = (await catalog.GetUnfilteredToolsAsync(http.RequestServices, ct))
                    .Any(t => t.Name == name);
                if (!exists)
                    return Results.NotFound(new { error = $"unknown tool '{name}'" });

                await ScopeAudit.RecordToolDeniedAsync(http.RequestServices, name, ct);
                return Results.Ok(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"tool '{name}' is not available for this credential" }],
                    IsError = true
                });
            }

            var arguments = await ReadArgumentsAsync(http, ct);
            var context = new ToolCallContext
            {
                Services = http.RequestServices,
                Arguments = arguments
            };

            var toolSw = System.Diagnostics.Stopwatch.StartNew();
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
            finally
            {
                Telemetry.KnowledgeHubMetrics.ToolDuration.Record(toolSw.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("tool", name));
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
