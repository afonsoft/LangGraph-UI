namespace KnowledgeHub.Server.Caching;

/// <summary>
/// SPEC-20260925-distributed-invalidation-pubsub RF-003: translates bus events
/// into local L1 evictions. <c>index-version</c> drops just the token key —
/// every cached result/answer key embeds it, so the next read re-fetches the
/// fresh token and all stale entries miss. <c>cache-clear</c> wipes L1.
/// </summary>
public sealed class InvalidationSubscriber : BackgroundService
{
    private readonly ICacheInvalidationBus _bus;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache _cache;
    private readonly ILogger<InvalidationSubscriber> _logger;

    public InvalidationSubscriber(
        ICacheInvalidationBus bus,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache cache,
        ILogger<InvalidationSubscriber> logger)
    {
        _bus = bus;
        _cache = cache;
        _logger = logger;
        _bus.Received += OnReceived;
    }

    private void OnReceived(object? sender, string topic)
    {
        if (_cache is not L1L2Cache l1)
            return; // no local tier to invalidate (memory provider: single process)

        switch (topic)
        {
            case "index-version":
                l1.InvalidateLocal(CacheKeys.IndexVersion);
                _logger.LogDebug("remote index-version bump — L1 token evicted");
                break;
            case "cache-clear":
                l1.InvalidateAllLocal();
                _logger.LogInformation("remote cache-clear — L1 compacted");
                break;
            default:
                // SPEC-20260926-cache-key-consistency RF-001: per-key eviction
                // propagated from another replica — drop only the L1 copy.
                if (topic.StartsWith("cache-key:", StringComparison.Ordinal))
                {
                    var key = topic["cache-key:".Length..];
                    l1.InvalidateLocal(key);
                    _logger.LogDebug("remote cache-key eviction — L1 entry dropped");
                }
                break;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Delay(Timeout.Infinite, stoppingToken); // event-driven only

    public override void Dispose()
    {
        _bus.Received -= OnReceived;
        base.Dispose();
    }
}
