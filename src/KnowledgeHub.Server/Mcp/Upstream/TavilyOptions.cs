namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>Tavily proxy configuration (SPEC-20260916-tavily-mcp-proxy).</summary>
public sealed class TavilyOptions
{
    public const string SectionName = "Tavily";

    /// <summary>When false, no tavily_* tools are exposed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Upstream MCP endpoint (Streamable HTTP; SDK auto-detects SSE fallback).
    /// The API key goes in the Authorization header — never in the URL query,
    /// which would leak it into access logs and proxies.</summary>
    public string Endpoint { get; set; } = "https://mcp.tavily.com/mcp";

    /// <summary>Optional API key (tvly-*) sent as Bearer; never logged/serialized.
    /// The DB-persisted secret (Settings screen) takes precedence when present.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Per-call timeout — tavily_crawl/research are long-running upstream.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>How long the upstream tools/list result is cached.</summary>
    public int ToolsCacheSeconds { get; set; } = 300;
}
