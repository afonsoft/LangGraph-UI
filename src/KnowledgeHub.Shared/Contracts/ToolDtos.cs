using System.Text.Json;

namespace KnowledgeHub.Shared.Contracts;

/// <summary>One catalog tool as exposed by GET /api/tools (SPEC-20260914-playground-tools RF-002).</summary>
public sealed record ToolDescriptorDto
{
    public required string Name { get; init; }
    /// <summary>SPEC-20260926-mcp-sdk-alignment RF-001: display title.</summary>
    public string? Title { get; init; }
    public required string Description { get; init; }
    /// <summary>JSON Schema for the tool's arguments (same object served over MCP tools/list).</summary>
    public required JsonElement InputSchema { get; init; }
    /// <summary>RF-002: JSON Schema of structuredContent, when the tool emits it.</summary>
    public JsonElement? OutputSchema { get; init; }
    public required bool ReadOnly { get; init; }
    /// <summary>RF-001: MCP annotation hints (null = omitted).</summary>
    public bool? DestructiveHint { get; init; }
    public bool? IdempotentHint { get; init; }
    public bool? OpenWorldHint { get; init; }
}

public sealed record ToolListResponse
{
    public required IReadOnlyList<ToolDescriptorDto> Tools { get; init; }
}
