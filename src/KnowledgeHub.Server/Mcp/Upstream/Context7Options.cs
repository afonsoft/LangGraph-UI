namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>Context7 proxy configuration (SPEC-20260922-context7-mcp-proxy).</summary>
public sealed class Context7Options
{
    public const string SectionName = "Context7";

    /// <summary>When false, no Context7 tools are exposed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Upstream MCP endpoint (Streamable HTTP; SDK auto-detects SSE fallback).
    /// The API key goes in the Authorization header — never in the URL query,
    /// which would leak it into access logs and proxies.</summary>
    public string Endpoint { get; set; } = "https://mcp.context7.com/mcp";

    /// <summary>Optional API key (ctx7sk-*) sent as Bearer; never logged/serialized.
    /// The DB-persisted secret (Settings screen) takes precedence when present.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Per-call timeout — documentation lookups are short upstream.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>How long the upstream tools/list result is cached.</summary>
    public int ToolsCacheSeconds { get; set; } = 300;
}
