namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>DeepWiki proxy configuration (SPEC-07 RF-005).</summary>
public sealed class DeepWikiOptions
{
    public const string SectionName = "DeepWiki";

    /// <summary>When false, the 3 proxy tools are not exposed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Upstream MCP endpoint (Streamable HTTP; SDK auto-detects SSE fallback).</summary>
    public string Endpoint { get; set; } = "https://mcp.deepwiki.com/mcp";

    /// <summary>Optional API key for DeepWiki private mode (sent as Bearer; never logged/serialized).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Per-call timeout — ask_question is LLM-backed upstream.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
