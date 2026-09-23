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
    public int ChunkIndex { get; set; }
    /// <summary>Comma-separated <c>SuspicionFlag</c> names.</summary>
    public required string Flags { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
