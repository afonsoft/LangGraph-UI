namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// SPEC-20260924-async-ingestion-queue RF-001: persisted sync/reindex job —
/// survives restarts (orphaned running jobs are failed at startup) and feeds
/// progress polling + audit.
/// </summary>
public sealed class IngestionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    /// <summary>sync | reindex (reindex forces re-chunk even on unchanged content).</summary>
    public string Kind { get; set; } = "sync";
    /// <summary>queued | running | done | failed | cancelled.</summary>
    public string Status { get; set; } = "queued";
    public int DocsProcessed { get; set; }
    public int DocsSkipped { get; set; }
    public int DocsFailed { get; set; }
    public int ChunksCreated { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public KnowledgeSource Source { get; set; } = null!;
}
