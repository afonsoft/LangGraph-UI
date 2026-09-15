using System.Diagnostics;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// SPEC-20260915-apikey-usage-audit RF-002/RF-005: after the pipeline runs,
/// persists an <see cref="ApiKeyUsageEvent"/> for every request whose principal
/// carries <c>auth_method=apikey</c>. Records are best-effort — a write failure
/// logs a warning and never fails the request. Retention (90 days or 10_000
/// events per key, whichever evicts first) is pruned on insert.
/// </summary>
public sealed class ApiKeyUsageMiddleware(RequestDelegate next, ILogger<ApiKeyUsageMiddleware> logger)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private const int MaxEventsPerKey = 10_000;
    private const int MaxPathLength = 256;
    private const int MaxUserAgentLength = 200;

    public async Task InvokeAsync(HttpContext context, KnowledgeHubDbContext db)
    {
        var stopwatch = Stopwatch.StartNew();
        await next(context);
        stopwatch.Stop();

        if (context.User.FindFirst(ApiKeyAuthenticationHandler.AuthMethodClaim)?.Value != "apikey"
            || !Guid.TryParse(context.User.FindFirst(ApiKeyAuthenticationHandler.KeyIdClaim)?.Value, out var keyId))
            return;

        try
        {
            var userAgent = context.Request.Headers.UserAgent.ToString();
            db.ApiKeyUsageEvents.Add(new ApiKeyUsageEvent
            {
                ApiKeyId = keyId,
                Timestamp = DateTimeOffset.UtcNow,
                HttpMethod = context.Request.Method,
                Path = Truncate(context.Request.Path.Value ?? "/", MaxPathLength),
                StatusCode = context.Response.StatusCode,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                UserAgent = string.IsNullOrEmpty(userAgent) ? null : Truncate(userAgent, MaxUserAgentLength)
            });

            // Audit write must not be cancelled with the request — the call
            // already happened and belongs in the log. RequestAborted may
            // already be cancelled by the time the pipeline returns.
            await PruneAsync(db, keyId, CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record api key usage event for key {KeyId}", keyId);
        }
    }

    // SQLite cannot translate DateTimeOffset comparisons — do the cutoff and
    // ordering in memory (the key is capped at MaxEventsPerKey rows anyway).
    private static async Task PruneAsync(KnowledgeHubDbContext db, Guid keyId, CancellationToken ct)
    {
        var stamps = await db.ApiKeyUsageEvents
            .Where(e => e.ApiKeyId == keyId)
            .Select(e => new { e.Id, e.Timestamp })
            .ToListAsync(ct);

        var cutoff = DateTimeOffset.UtcNow - Retention;
        // the new event (pending insert) is not in stamps — keep room for it
        var removeCount = Math.Max(
            stamps.Count(e => e.Timestamp < cutoff),
            stamps.Count - (MaxEventsPerKey - 1));
        if (removeCount <= 0)
            return;

        var removeIds = stamps
            .OrderBy(e => e.Timestamp)
            .Take(removeCount)
            .Select(e => e.Id)
            .ToList();
        await db.ApiKeyUsageEvents
            .Where(e => removeIds.Contains(e.Id))
            .ExecuteDeleteAsync(ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
