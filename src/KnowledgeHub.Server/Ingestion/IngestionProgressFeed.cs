using System.Collections.Concurrent;

namespace KnowledgeHub.Server.Ingestion;

/// <summary>One progress tick or terminal state of an ingestion job
/// (SPEC-20260925-job-progress-feed). Counters only — never doc content.</summary>
public sealed record IngestionProgressEvent(
    Guid JobId,
    Guid SourceId,
    string Status,
    int Processed,
    int Skipped,
    int Failed,
    int ChunksCreated,
    DateTimeOffset Timestamp);

/// <summary>Pub/sub channel for ingestion progress — fanned out to SignalR
/// clients by <see cref="Hubs.IngestionProgressBroadcastService"/>.</summary>
public interface IIngestionProgressFeed
{
    void Publish(IngestionProgressEvent e);
    event Action<IngestionProgressEvent>? Published;
}

/// <summary>Throttled in-memory feed: running-status events are capped at one
/// per second per job; terminal states always pass.</summary>
public sealed class IngestionProgressFeed : IIngestionProgressFeed
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastPublish = new();

    public event Action<IngestionProgressEvent>? Published;

    public void Publish(IngestionProgressEvent e)
    {
        if (e.Status == "running")
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastPublish.TryGetValue(e.JobId, out var last)
                && now - last < TimeSpan.FromSeconds(1))
                return;
            _lastPublish[e.JobId] = now;
        }
        else
        {
            _lastPublish.TryRemove(e.JobId, out _);
        }
        Published?.Invoke(e);
    }
}
