using System.Text.Json.Serialization;

namespace KnowledgeHub.Shared.Contracts;

/// <summary>Supported knowledge source connector types (SPEC-02 RF-001).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceType
{
    WebPage = 1,
    ObsidianVault = 2,
    DocumentFile = 3,
    RestApi = 4,
    SqlDatabase = 5,
    /// <summary>Upstream MCP server exposed through the tool catalog
    /// (SPEC-20260917-mcp-proxy-source-type) — proxy only, not ingestible.</summary>
    McpProxy = 6
}
