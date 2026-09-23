namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// One audit row per flagged chunk detection
/// (SPEC-20260923-prompt-injection-guard RF-003). Stores ids and flag names
/// only — never chunk content.
/// </summary>
public sealed class SecurityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SourceId { get; set; }
    public Guid? DocumentId { get; set; }
    /// <summary>Chunk ordinal; <c>-1</c> for events not tied to a chunk
    /// (SPEC-20260923-source-authorization RF-005 scope denials).</summary>
    public int ChunkIndex { get; set; }
    /// <summary>Comma-separated <c>SuspicionFlag</c> names or the denial kind
    /// (<c>SourceScopeDenied</c>/<c>ToolScopeDenied</c>).</summary>
    public required string Flags { get; set; }
    /// <summary>SPEC-20260923-source-authorization RF-005: presenting key for
    /// scope-denial events; null for ingestion-time detections.</summary>
    public Guid? ApiKeyId { get; set; }
    /// <summary>Short non-content detail (e.g. denied tool name). Never raw content.</summary>
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
