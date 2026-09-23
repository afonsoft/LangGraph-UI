namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Single-row GraphRAG override (SPEC-20260923-graph-settings-ui RF-001):
/// master switch + tuning knobs editable from /settings without redeploy.
/// Absent row → the Graph:* configuration keys and built-in defaults apply.
/// </summary>
public sealed class GraphSettings
{
    /// <summary>Single-row table — the service always upserts row Id = 1.</summary>
    public int Id { get; set; }
    /// <summary>Master switch: gates the graph tools catalog and ingestion extraction.</summary>
    public bool Enabled { get; set; }
    /// <summary>Max chunks fed to the LLM extractor per sync run.</summary>
    public int MaxChunksPerSync { get; set; }
    /// <summary>Per-chunk character cap sent to the extraction prompt.</summary>
    public int MaxChunkChars { get; set; }
    /// <summary>Hard cap on edges returned by a single traversal.</summary>
    public int MaxResults { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
