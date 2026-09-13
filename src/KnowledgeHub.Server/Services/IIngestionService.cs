using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// Runs ingestion for a source (SPEC-03 implements the Obsidian connector).
/// Registered as a no-op stub until then — other connector types return "skipped".
/// </summary>
public interface IIngestionService
{
    Task<SyncResultDto> SyncAsync(Guid sourceId, CancellationToken cancellationToken = default);
}

/// <summary>Placeholder until SPEC-03 lands: every sync reports "skipped".</summary>
public sealed class NotImplementedIngestionService : IIngestionService
{
    public Task<SyncResultDto> SyncAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SyncResultDto
        {
            Status = "skipped",
            Reason = "Ingestion connector not implemented yet (SPEC-03)"
        });
}
