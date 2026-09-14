using System.Text.Json;

namespace KnowledgeHub.Shared.Contracts;

/// <summary>One catalog tool as exposed by GET /api/tools (SPEC-20260914-playground-tools RF-002).</summary>
public sealed record ToolDescriptorDto
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>JSON Schema for the tool's arguments (same object served over MCP tools/list).</summary>
    public required JsonElement InputSchema { get; init; }
    public required bool ReadOnly { get; init; }
}

public sealed record ToolListResponse
{
    public required IReadOnlyList<ToolDescriptorDto> Tools { get; init; }
}
