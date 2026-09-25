using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.Server.Ingestion.Staging;

/// <summary>
/// Default implementation of <see cref="IStagingStorageService"/> writing to data/staging/{sourceId}
/// (SPEC-20260924-cloud-storage-connectors RF-002).
/// </summary>
public sealed class StagingStorageService : IStagingStorageService
{
    private readonly string _baseStagingPath;
    private readonly ILogger<StagingStorageService> _logger;

    public StagingStorageService(IConfiguration config, ILogger<StagingStorageService> logger)
    {
        _logger = logger;
        var custom = config["Staging:Path"];
        _baseStagingPath = !string.IsNullOrWhiteSpace(custom)
            ? Path.GetFullPath(custom)
            : Path.Combine(AppContext.BaseDirectory, "data", "staging");
    }

    public string GetStagingDirectory(Guid sourceId)
    {
        var path = Path.Combine(_baseStagingPath, sourceId.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public Task CleanupStagingAsync(Guid sourceId, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_baseStagingPath, sourceId.ToString("N"));
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                _logger.LogInformation("Cleaned up staging directory for source {SourceId} at {Path}", sourceId, path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up staging directory for source {SourceId}", sourceId);
        }
        return Task.CompletedTask;
    }
}
