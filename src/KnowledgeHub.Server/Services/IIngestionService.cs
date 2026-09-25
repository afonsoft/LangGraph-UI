using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

public interface IIngestionService
{
    Task<SyncResultDto> SyncAsync(
        Guid sourceId, SyncOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// SPEC-20260924-async-ingestion-queue: per-run options for a sync.
/// </summary>
public sealed record SyncOptions
{
    /// <summary>Reindex mode — re-chunks/re-embeds even content-unchanged docs.</summary>
    public bool ForceReindex { get; init; }
    /// <summary>Per-document progress sink — invoked after each processed doc.</summary>
    public IProgress<SyncProgress>? Progress { get; init; }
}

/// <summary>Progress tick reported per processed document.</summary>
public sealed record SyncProgress(int Processed, int Skipped, int Failed, int ChunksCreated);
