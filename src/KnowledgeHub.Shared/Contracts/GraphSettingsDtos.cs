namespace KnowledgeHub.Shared.Contracts;

/// <summary>Effective GraphRAG settings for the Settings UI
/// (SPEC-20260923-graph-settings-ui RF-004). Plain scalars — no secrets.</summary>
public sealed record GraphSettingsDto
{
    /// <summary>Master switch: gates the graph tools catalog and ingestion extraction.</summary>
    public required bool Enabled { get; init; }
    /// <summary>Max chunks fed to the LLM extractor per sync run.</summary>
    public required int MaxChunksPerSync { get; init; }
    /// <summary>Per-chunk character cap sent to the extraction prompt.</summary>
    public required int MaxChunkChars { get; init; }
    /// <summary>Hard cap on edges returned by a single traversal.</summary>
    public required int MaxResults { get; init; }
    /// <summary>Where the effective values come from: "store" | "env".</summary>
    public required string Source { get; init; }
    /// <summary>True when env/config supplies at least one Graph:* key.</summary>
    public required bool EnvConfigured { get; init; }
    /// <summary>Last update of the stored override, null when Source != store.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>PUT /api/settings/graph body — all bounds validated server-side.</summary>
public sealed record SaveGraphSettingsRequest
{
    public required bool Enabled { get; init; }
    public required int MaxChunksPerSync { get; init; }
    public required int MaxChunkChars { get; init; }
    public required int MaxResults { get; init; }
}
