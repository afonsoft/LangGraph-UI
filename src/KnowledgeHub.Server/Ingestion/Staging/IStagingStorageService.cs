namespace KnowledgeHub.Server.Ingestion.Staging;

/// <summary>
/// Manages local staging disk directories for remote sync sources
/// (SPEC-20260924-cloud-storage-connectors RF-002).
/// </summary>
public interface IStagingStorageService
{
    /// <summary>Gets the absolute staging path for a given source, ensuring directory exists.</summary>
    string GetStagingDirectory(Guid sourceId);

    /// <summary>Deletes the staging directory and all downloaded files for a source.</summary>
    Task CleanupStagingAsync(Guid sourceId, CancellationToken ct = default);

    /// <summary>Removes staging directories whose source id is not in
    /// <paramref name="knownSourceIds"/>. Returns removed count.</summary>
    Task<int> CleanupOrphanedStagingAsync(IReadOnlySet<Guid> knownSourceIds, CancellationToken ct = default);
}
