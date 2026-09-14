using System.Text.Json;
using KnowledgeHub.McpEngine.Activity;
using KnowledgeHub.Server.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Agent;

/// <summary>
/// Adapts a live <see cref="CatalogTool"/> to an <see cref="AIFunction"/> so the
/// chat client can drive the same handlers MCP/REST use (SPEC-20260914-agent-chat-loop T1).
/// Results are truncated to <c>Agent:MaxToolResultChars</c> before re-entering the model.
/// </summary>
public sealed class CatalogToolAIFunction(
    CatalogTool tool,
    IServiceProvider services,
    int maxResultChars) : AIFunction
{
    public override string Name => tool.Name;
    public override string Description => tool.Description;
    public override JsonElement JsonSchema => JsonSerializer.SerializeToElement(tool.InputSchema);

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in arguments)
            args[key] = value is JsonElement el
                ? el
                : JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);

        var started = System.Diagnostics.Stopwatch.StartNew();
        string? error = null;
        CallToolResult result;
        try
        {
            result = await tool.Handler(
                new ToolCallContext { Services = services, Arguments = args }, cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"ERROR: {ex.Message}" }],
                IsError = true
            };
        }

        // RF-003: every internal tool call is observable on the MCP Monitor feed.
        services.GetService<IMcpActivityFeed>()?.Record(new McpActivityEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = McpActivityKind.ToolCall,
            Transport = "agent",
            Method = "agent_chat",
            ToolName = tool.Name,
            DurationMs = started.Elapsed.TotalMilliseconds,
            Succeeded = result.IsError != true,
            Error = error
        });

        var text = string.Join("\n",
            result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        if (text.Length > maxResultChars)
            text = string.Concat(text.AsSpan(0, maxResultChars), "…[truncated]");
        return result.IsError == true ? $"ERROR: {text}" : text;
    }
}
