using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// SPEC-20260923-source-authorization RF-005: every scope denial writes a
/// <see cref="SecurityEvent"/> — key id, denial kind and the denied
/// source/tool name. Content is never recorded.
/// </summary>
public static class ScopeAudit
{
    public const string SourceDenied = "SourceScopeDenied";
    public const string ToolDenied = "ToolScopeDenied";

    public static async Task RecordAsync(
        KnowledgeHubDbContext db, Guid? apiKeyId, string kind,
        Guid? sourceId = null, string? detail = null, CancellationToken ct = default)
    {
        if (apiKeyId is null)
            return; // cookie principals are unrestricted — nothing denied
        db.SecurityEvents.Add(new SecurityEvent
        {
            ApiKeyId = apiKeyId,
            SourceId = sourceId,
            ChunkIndex = -1,
            Flags = kind,
            Detail = detail is { Length: > 200 } ? detail[..200] : detail
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resolves scope + db from a request service provider — used by
    /// the MCP/REST tool dispatchers that don't hold them directly.</summary>
    public static async Task RecordToolDeniedAsync(
        IServiceProvider services, string toolName, CancellationToken ct)
    {
        var scope = services.GetService<ICallerScopeProvider>() is { } p
            ? await p.GetAsync(ct)
            : CallerScope.Unrestricted;
        if (services.GetService<KnowledgeHubDbContext>() is { } db)
            await RecordAsync(db, scope.ApiKeyId, ToolDenied, detail: toolName, ct: ct);
    }
}
