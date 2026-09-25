using System.Collections.Concurrent;
using System.Threading.Channels;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Ingestion;

/// <summary>
/// SPEC-20260924-async-ingestion-queue RF-001: bounded channel + persisted jobs.
/// Enqueue is deduplicated per source — a second sync while one is
/// queued/running returns the existing job instead of piling on.
/// </summary>
public interface IIngestionQueue
{
    /// <summary>Creates (or dedup-returns) a job. Throws <see cref="QueueFullException"/>
    /// when the bounded channel cannot accept work.</summary>
    Task<IngestionJobEnqueueResult> EnqueueAsync(Guid sourceId, string kind, CancellationToken ct);

    /// <summary>Reads pending job ids — consumed by <see cref="IngestionWorker"/>.</summary>
    ChannelReader<Guid> Reader { get; }

    /// <summary>Requests cancellation of a queued/running job.</summary>
    bool TryCancel(Guid jobId);

    /// <summary>Cancellation token linked to job cancellation.</summary>
    CancellationToken TokenFor(Guid jobId, CancellationToken stopping);

    /// <summary>Drops the cancellation registration when a job finishes.</summary>
    void Complete(Guid jobId);
}

public sealed class QueueFullException : Exception
{
    public QueueFullException() : base("ingestion queue is full") { }
}

public sealed record IngestionJobEnqueueResult(IngestionJob Job, bool Existing);

public sealed class IngestionQueue(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IIngestionQueue
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancelTokens = new();
    private Channel<Guid>? _channel;

    private Channel<Guid> Channel => _channel ??= System.Threading.Channels.Channel
        .CreateBounded<Guid>(new BoundedChannelOptions(
            configuration.GetValue("Ingestion:QueueSize", 100))
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    public ChannelReader<Guid> Reader => Channel.Reader;

    public async Task<IngestionJobEnqueueResult> EnqueueAsync(
        Guid sourceId, string kind, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();

        // Dedup: an already queued/running job for the same source is returned.
        // SQLite cannot ORDER BY DateTimeOffset — pick the newest in memory
        // (queued/running jobs per source are rare).
        var existing = (await db.IngestionJobs
                .Where(j => j.SourceId == sourceId && (j.Status == "queued" || j.Status == "running"))
                .ToListAsync(ct))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefault();
        if (existing is not null)
            return new IngestionJobEnqueueResult(existing, Existing: true);

        var job = new IngestionJob { SourceId = sourceId, Kind = kind };
        db.IngestionJobs.Add(job);
        await db.SaveChangesAsync(ct);

        if (!Channel.Writer.TryWrite(job.Id))
        {
            // RF-007 (SPEC-20260926-ingestion-connector-integrity): a persisted
            // "queued" job that never reached the channel would deadlock dedup —
            // every future enqueue returns it as `Existing`. Mark it failed so
            // the next cycle enqueues normally.
            job.Status = "failed";
            job.Error = "ingestion queue is full";
            job.FinishedAt = DateTimeOffset.UtcNow;
            // RF-206: repair must not inherit the request token — a client
            // disconnect between the persist and this save would otherwise
            // leave "queued" forever and deadlock the dedup check.
            await db.SaveChangesAsync(CancellationToken.None);
            throw new QueueFullException();
        }
        return new IngestionJobEnqueueResult(job, Existing: false);
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_cancelTokens.TryGetValue(jobId, out var cts))
            return false;
        cts.Cancel();
        return true;
    }

    public CancellationToken TokenFor(Guid jobId, CancellationToken stopping)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _cancelTokens[jobId] = cts;
        return cts.Token;
    }

    public void Complete(Guid jobId) => _cancelTokens.TryRemove(jobId, out _);
}
