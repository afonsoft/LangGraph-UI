namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>Firecrawl proxy configuration (SPEC-20260916-firecrawl-mcp-proxy).</summary>
public sealed class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>When false, no firecrawl_* tools are exposed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Upstream MCP endpoint (Streamable HTTP; SDK auto-detects SSE fallback).</summary>
    public string Endpoint { get; set; } = "https://mcp.firecrawl.dev/v2/mcp";

    /// <summary>Optional API key (fc-*) sent as Bearer; never logged/serialized.
    /// The DB-persisted secret (Settings screen) takes precedence when present.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Per-call timeout — crawl/agent are long-running upstream.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>How long the upstream tools/list result is cached.</summary>
    public int ToolsCacheSeconds { get; set; } = 300;
}
