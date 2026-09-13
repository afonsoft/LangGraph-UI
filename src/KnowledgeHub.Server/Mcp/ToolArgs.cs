using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgeHub.Server.Mcp;

/// <summary>Argument extraction + validation helpers for dynamic tools (SPEC-04 RF-002).</summary>
public static class ToolArgs
{
    public static string RequiredString(RequestContext<CallToolRequestParams> ctx, string name) =>
        OptionalString(ctx, name) is { Length: > 0 } value
            ? value
            : throw new McpProtocolException($"missing required argument '{name}'", McpErrorCode.InvalidParams);

    public static string? OptionalString(RequestContext<CallToolRequestParams> ctx, string name) =>
        TryGet(ctx, name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    public static int OptionalInt(RequestContext<CallToolRequestParams> ctx, string name, int fallback, int max)
    {
        if (!TryGet(ctx, name, out var el))
            return fallback;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var value))
            throw new McpProtocolException($"argument '{name}' must be an integer", McpErrorCode.InvalidParams);
        if (value <= 0)
            return fallback;
        return Math.Min(value, max);
    }

    public static string[]? OptionalStringArray(RequestContext<CallToolRequestParams> ctx, string name)
    {
        if (!TryGet(ctx, name, out var el) || el.ValueKind != JsonValueKind.Array)
            return null;
        return el.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
    }

    private static bool TryGet(RequestContext<CallToolRequestParams> ctx, string name, out JsonElement element)
    {
        if (ctx.Params?.Arguments is not null && ctx.Params.Arguments.TryGetValue(name, out var el))
        {
            element = el;
            return true;
        }
        element = default;
        return false;
    }
}

/// <summary>Helpers for building CallToolResult payloads.</summary>
public static class ToolResults
{
    public static ValueTask<CallToolResult> Text(string text) =>
        ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            IsError = false
        });

    public static ValueTask<CallToolResult> Error(string message) =>
        ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            IsError = true
        });
}
