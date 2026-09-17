namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>DeepWiki proxy configuration (SPEC-07 RF-005).</summary>
public sealed class DeepWikiOptions
{
    public const string SectionName = "DeepWiki";

    /// <summary>When false, the 3 proxy tools are not exposed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Public upstream MCP endpoint — used when no API key is configured
    /// (Streamable HTTP; SDK auto-detects SSE fallback).</summary>
    public string Endpoint { get; set; } = "https://mcp.deepwiki.com/mcp";

    /// <summary>Private-mode upstream MCP endpoint — used when an API key is configured
    /// (SPEC-20260916-firecrawl-mcp-proxy RF-008).</summary>
    public string PrivateEndpoint { get; set; } = "https://mcp.devin.ai/mcp";

    /// <summary>Optional API key for DeepWiki private mode (sent as Bearer; never logged/serialized).
    /// The DB-persisted secret (Settings screen) takes precedence when present.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Per-call timeout — ask_question is LLM-backed upstream.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>TTL for the cached upstream tools/list used by the dynamic
    /// merge in private mode (SPEC-20260917-upstream-tools-passthrough).</summary>
    public int ToolsCacheSeconds { get; set; } = 300;
}
