using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// A dynamically-generated MCP tool: protocol metadata plus its invoke handler.
/// Built per <c>tools/list</c> request so the catalog always mirrors live DB state
/// (SPEC-04 RF-001).
/// </summary>
public sealed record CatalogTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonObject InputSchema { get; init; }
    public bool ReadOnly { get; init; }
    public required Func<ToolCallContext, CancellationToken, ValueTask<CallToolResult>> Handler { get; init; }
}
