using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260924-async-ingestion-queue: enqueue dedup, cancellation and
/// channel delivery.
/// </summary>
public sealed class IngestionQueueTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;
    private readonly IngestionQueue _queue;

    public IngestionQueueTests()
    {
        _conn.Open();
        var services = new ServiceCollection();
        services.AddDbContext<KnowledgeHubDbContext>(o => o.UseSqlite(_conn));
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>()
            .Database.EnsureCreated();

        _queue = new IngestionQueue(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build());
    }

    private async Task<Guid> SeedSourceAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var source = new KnowledgeSource { Name = "s", SourceType = SourceType.ObsidianVault };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    [Fact]
    public async Task Enqueue_CreatesQueuedJob_AndDeliversOnChannel()
    {
        var sourceId = await SeedSourceAsync();

        var (job, existing) = await _queue.EnqueueAsync(sourceId, "sync", CancellationToken.None);

        Assert.False(existing);
        Assert.Equal("queued", job.Status);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var delivered = await _queue.Reader.ReadAsync(cts.Token);
        Assert.Equal(job.Id, delivered);
    }

    [Fact]
    public async Task Enqueue_SecondWhileQueued_ReturnsExistingJob()
    {
        var sourceId = await SeedSourceAsync();

        var first = await _queue.EnqueueAsync(sourceId, "sync", CancellationToken.None);
        var second = await _queue.EnqueueAsync(sourceId, "sync", CancellationToken.None);

        Assert.True(second.Existing);
        Assert.Equal(first.Job.Id, second.Job.Id);
    }

    [Fact]
    public async Task Enqueue_AfterTerminalStatus_CreatesNewJob()
    {
        var sourceId = await SeedSourceAsync();
        var first = await _queue.EnqueueAsync(sourceId, "sync", CancellationToken.None);

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            var row = await db.IngestionJobs.FindAsync(first.Job.Id);
            row!.Status = "done";
            await db.SaveChangesAsync();
        }

        var second = await _queue.EnqueueAsync(sourceId, "reindex", CancellationToken.None);
        Assert.False(second.Existing);
        Assert.NotEqual(first.Job.Id, second.Job.Id);
        Assert.Equal("reindex", second.Job.Kind);
    }

    [Fact]
    public void TryCancel_UnknownJob_ReturnsFalse()
    {
        Assert.False(_queue.TryCancel(Guid.NewGuid()));
    }

    [Fact]
    public void TokenFor_CancelledByTryCancel()
    {
        var jobId = Guid.NewGuid();
        var token = _queue.TokenFor(jobId, CancellationToken.None);

        Assert.True(_queue.TryCancel(jobId));
        Assert.True(token.IsCancellationRequested);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }
}
