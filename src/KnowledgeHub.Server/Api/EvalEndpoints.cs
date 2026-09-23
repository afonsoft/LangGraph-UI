using System.Text.Json;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Eval;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// SPEC-20260923-eval-harness RF-004: eval endpoints — POST /api/eval/run
/// executes a dataset (raw JSON array or a name under tests/eval/),
/// GET /api/eval/runs lists, GET /api/eval/runs/{id}?compare= diffs.
/// </summary>
public static class EvalEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record EvalRunRequest
    {
        /// <summary>Raw JSON array of cases, or a dataset name resolved under tests/eval/.</summary>
        public required string Dataset { get; init; }
        public int? TopK { get; init; }
        public string? Mode { get; init; }
        public string? Faithfulness { get; init; }
        public Guid? CompareTo { get; init; }
    }

    public static RouteGroupBuilder MapEvalApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/eval");

        group.MapPost("/run", async (
            EvalRunRequest request, EvalRunner runner,
            IWebHostEnvironment env, CancellationToken ct) =>
        {
            var (datasetJson, resolveError) = ResolveDataset(request.Dataset, env.ContentRootPath);
            if (resolveError is not null)
                return Results.BadRequest(new { error = resolveError });

            var (cases, errors) = EvalDataset.Parse(datasetJson!);
            if (errors.Count > 0)
                return Results.BadRequest(new { error = "malformed dataset", errors });

            try
            {
                var report = await runner.RunAsync(
                    cases, datasetJson!, request.Mode, request.TopK,
                    request.Faithfulness, request.CompareTo, ct);
                return Results.Ok(report);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        group.MapGet("/runs", async (KnowledgeHubDbContext db, int? limit, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            // SQLite cannot ORDER BY DateTimeOffset — sort in memory (runs are rare).
            var runs = (await db.EvalRuns.AsNoTracking()
                    .Select(r => new { r.Id, r.StartedAt, r.DurationMs, r.DatasetHash, r.MetricsJson })
                    .ToListAsync(ct))
                .OrderByDescending(r => r.StartedAt)
                .Take(take);
            return Results.Ok(runs.Select(r => new
            {
                r.Id,
                r.StartedAt,
                r.DurationMs,
                r.DatasetHash,
                metrics = JsonSerializer.Deserialize<EvalMetricsSummary>(r.MetricsJson, Json)
            }));
        });

        group.MapGet("/runs/{id:guid}", async (
            Guid id, string? compare, KnowledgeHubDbContext db, CancellationToken ct) =>
        {
            var run = await db.EvalRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run is null)
                return Results.NotFound(new { error = "run not found" });

            var results = JsonSerializer.Deserialize<List<EvalCaseResult>>(run.PerCaseJson, Json) ?? [];
            var metrics = JsonSerializer.Deserialize<EvalMetricsSummary>(run.MetricsJson, Json)!;
            EvalDelta? delta = null;
            if (Guid.TryParse(compare, out var prevId))
            {
                var prev = await db.EvalRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == prevId, ct);
                if (prev is null)
                    return Results.NotFound(new { error = $"compare run '{prevId}' not found" });
                var prevMetrics = JsonSerializer.Deserialize<EvalMetricsSummary>(prev.MetricsJson, Json)!;
                var prevCases = JsonSerializer.Deserialize<List<EvalCaseResult>>(prev.PerCaseJson, Json) ?? [];
                var prevHits = prevCases.Where(c => c.Hit).Select(c => c.CaseId).ToHashSet();
                delta = new EvalDelta
                {
                    CompareRunId = prevId,
                    RecallAtKDelta = EvalMetrics.Round(metrics.RecallAtK - prevMetrics.RecallAtK),
                    PrecisionAtKDelta = EvalMetrics.Round(metrics.PrecisionAtK - prevMetrics.PrecisionAtK),
                    MrrDelta = EvalMetrics.Round(metrics.Mrr - prevMetrics.Mrr),
                    Regressions = results.Where(r => !r.Hit && prevHits.Contains(r.CaseId))
                        .Select(r => r.CaseId).ToList()
                };
            }

            return Results.Ok(new EvalReport
            {
                RunId = run.Id,
                StartedAt = run.StartedAt,
                DurationMs = run.DurationMs,
                DatasetHash = run.DatasetHash,
                Cases = results.Count,
                Metrics = metrics,
                Results = results,
                Delta = delta
            });
        });

        return group;
    }

    /// <summary>
    /// Resolves the dataset: inline JSON array, or a name looked up under
    /// <c>tests/eval/</c> walking up from the content root (repo layout).
    /// </summary>
    private static (string? Json, string? Error) ResolveDataset(string dataset, string contentRoot)
    {
        var trimmed = dataset.Trim();
        if (trimmed.StartsWith('['))
            return (trimmed, null);

        if (trimmed.IndexOfAny(new[] { '/', '\\', '.' }) >= 0 || trimmed.Length > 100)
            return (null, "dataset name must not contain path separators or dots");

        var dir = new DirectoryInfo(contentRoot);
        for (var i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "eval", $"{trimmed}.json");
            if (File.Exists(candidate))
                return (File.ReadAllText(candidate), null);
        }
        return (null, $"dataset '{trimmed}' not found under tests/eval/");
    }
}
