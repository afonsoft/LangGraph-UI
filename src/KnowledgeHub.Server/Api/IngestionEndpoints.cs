using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// SPEC-20260924-async-ingestion-queue RF-001: ingestion job inspection +
/// cancellation endpoints.
/// </summary>
public static class IngestionEndpoints
{
    public static RouteGroupBuilder MapIngestionApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingestion/jobs");

        group.MapGet("/", async (
            KnowledgeHubDbContext db, Guid? sourceId, int? limit, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            var query = db.IngestionJobs.AsNoTracking().AsQueryable();
            if (sourceId is { } sid)
                query = query.Where(j => j.SourceId == sid);
            // SQLite cannot ORDER BY DateTimeOffset — sort in memory
            // (job rows are bounded by queue churn).
            var jobs = (await query.ToListAsync(ct))
                .OrderByDescending(j => j.CreatedAt)
                .Take(take)
                .Select(j => new
                {
                    j.Id, j.SourceId, j.Kind, j.Status,
                    j.DocsProcessed, j.DocsSkipped, j.DocsFailed, j.ChunksCreated,
                    j.Error, j.CreatedAt, j.StartedAt, j.FinishedAt
                });
            return Results.Ok(jobs);
        });

        group.MapGet("/{id:guid}", async (KnowledgeHubDbContext db, Guid id, CancellationToken ct) =>
        {
            var job = await db.IngestionJobs.AsNoTracking()
                .Where(j => j.Id == id)
                .Select(j => new
                {
                    j.Id, j.SourceId, j.Kind, j.Status,
                    j.DocsProcessed, j.DocsSkipped, j.DocsFailed, j.ChunksCreated,
                    j.Error, j.CreatedAt, j.StartedAt, j.FinishedAt
                })
                .FirstOrDefaultAsync(ct);
            return job is null ? Results.NotFound(new { error = "job not found" }) : Results.Ok(job);
        });

        group.MapPost("/{id:guid}/cancel", async (
            IIngestionQueue queue, KnowledgeHubDbContext db, Guid id, CancellationToken ct) =>
        {
            var job = await db.IngestionJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null)
                return Results.NotFound(new { error = "job not found" });
            if (job.Status == "queued")
            {
                job.Status = "cancelled";
                job.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                queue.TryCancel(id);
                return Results.Ok(new { id, status = "cancelled" });
            }
            if (job.Status == "running")
            {
                queue.TryCancel(id);
                return Results.Accepted($"/api/ingestion/jobs/{id}", new { id, status = "cancelling" });
            }
            return Results.Conflict(new { error = $"job is already {job.Status}" });
        });

        return group;
    }
}
