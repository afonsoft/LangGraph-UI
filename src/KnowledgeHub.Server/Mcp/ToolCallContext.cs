using System.Text.Json;

namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// Transport-neutral invocation context for <see cref="CatalogTool.Handler"/>
/// (SPEC-20260914-playground-tools RF-001): the MCP CallToolHandler adapts
/// <c>RequestContext</c> into this, and REST tool endpoints build it directly —
/// the same handler serves both.
/// </summary>
public sealed record ToolCallContext
{
    public required IServiceProvider Services { get; init; }
    public IDictionary<string, JsonElement>? Arguments { get; init; }
}
