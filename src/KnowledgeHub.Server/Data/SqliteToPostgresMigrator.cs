using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Data;

/// <summary>
/// SPEC-20260926-unified-database-provider RF-005: one-shot catalog copy from the
/// legacy SQLite file into Postgres on first boot with an empty catalog.
/// GUID keys are preserved so the existing <c>kh_embeddings.chunk_id</c> rows
/// stay joined to their chunks after the move.
/// </summary>
public static class SqliteToPostgresMigrator
{
    public static async Task RunAsync(
        KnowledgeHubDbContext pg, IConfiguration cfg, ILogger logger, CancellationToken ct = default)
    {
        var path = DatabasePath.Resolve(cfg);
        if (!File.Exists(path))
            return;
        if (File.Exists(path + ".migrated"))
        {
            logger.LogInformation("SQLite catalog already migrated to Postgres — skipping ({Marker})", path + ".migrated");
            return;
        }
        if (await pg.Sources.AnyAsync(ct) || await pg.Users.AnyAsync(ct))
        {
            logger.LogInformation("Postgres catalog already populated — skipping SQLite backfill");
            return;
        }

        var sqliteOptions = new DbContextOptionsBuilder<KnowledgeHubDbContext>()
            .UseSqlite($"Data Source={path};Mode=ReadOnly")
            .Options;
        await using var sqlite = new KnowledgeHubDbContext(sqliteOptions);

        // One transaction covering all inserts: a crash mid-copy must roll back
        // so the retry sees an empty catalog and reruns — a partial copy would
        // otherwise trip the populated-guard and leave a half-migrated catalog.
        await using var tx = await pg.Database.BeginTransactionAsync(ct);

        // Parents before children — FK-safe order. Children are filtered to
        // parents that actually exist in Postgres: the SQLite file predates
        // enforced foreign keys (PRAGMA foreign_keys was off historically), so
        // orphan rows (e.g. usage events of deleted API keys) must be skipped —
        // Postgres rejects them with 23503 (hit in production deploy).
        var migrated = 0;
        migrated += await CopyAsync(sqlite.Sources, pg, ct);
        migrated += await CopyAsync(sqlite.Users, pg, ct);
        var sourceIds = await IdsOfAsync(pg.Sources, s => s.Id, ct);
        var userIds = await IdsOfAsync(pg.Users, u => u.Id, ct);
        migrated += await CopyAsync(sqlite.ApiKeys, pg, ct, k => userIds.Contains(k.UserId));
        var apiKeyIds = await IdsOfAsync(pg.ApiKeys, k => k.Id, ct);
        var docIds = new HashSet<Guid>();
        migrated += await CopyAsync(sqlite.Documents, pg, ct,
            d => sourceIds.Contains(d.KnowledgeSourceId));
        foreach (var id in await pg.Documents.Select(d => d.Id).ToListAsync(ct))
            docIds.Add(id);
        migrated += await CopyAsync(sqlite.Chunks, pg, ct,
            c => docIds.Contains(c.KnowledgeDocumentId));
        migrated += await CopyAsync(sqlite.Threads, pg, ct);
        var threadIds = await IdsOfAsync(pg.Threads, t => t.Id, ct);
        migrated += await CopyAsync(sqlite.ThreadMessages, pg, ct,
            m => threadIds.Contains(m.ThreadId));
        migrated += await CopyAsync(sqlite.Approvals, pg, ct);
        migrated += await CopyAsync(sqlite.ApiKeyUsageEvents, pg, ct,
            e => apiKeyIds.Contains(e.ApiKeyId));
        migrated += await CopyAsync(sqlite.ApiKeyChatSettings, pg, ct,
            s => apiKeyIds.Contains(s.ApiKeyId));
        migrated += await CopyAsync(sqlite.IntegrationSecrets, pg, ct);
        migrated += await CopyAsync(sqlite.ChatSettings, pg, ct);
        migrated += await CopyAsync(sqlite.EmbeddingSettings, pg, ct);
        migrated += await CopyAsync(sqlite.GraphSettings, pg, ct);
        migrated += await CopyAsync(sqlite.EvalRuns, pg, ct);
        migrated += await CopyAsync(sqlite.EvalBaselines, pg, ct);
        migrated += await CopyAsync(sqlite.IngestionJobs, pg, ct,
            j => sourceIds.Contains(j.SourceId));
        migrated += await CopyAsync(sqlite.KgNodes, pg, ct);
        var nodeIds = await IdsOfAsync(pg.KgNodes, n => n.Id, ct);
        migrated += await CopyAsync(sqlite.KgAliases, pg, ct,
            a => nodeIds.Contains(a.KgNodeId));
        migrated += await CopyAsync(sqlite.KgEdges, pg, ct,
            x => nodeIds.Contains(x.FromNodeId) && nodeIds.Contains(x.ToNodeId)
                 && docIds.Contains(x.KnowledgeDocumentId));
        migrated += await CopyAsync(sqlite.SecurityEvents, pg, ct);

        await tx.CommitAsync(ct);
        try { await File.WriteAllTextAsync(path + ".migrated", DateTimeOffset.UtcNow.ToString("O"), ct); }
        catch { /* marker is informational only */ }
        logger.LogInformation("SQLite catalog migrated to Postgres: {Count} rows copied", migrated);
    }

    private static async Task<HashSet<Guid>> IdsOfAsync<TEntity>(
        IQueryable<TEntity> set,
        System.Linq.Expressions.Expression<Func<TEntity, Guid>> key,
        CancellationToken ct)
        where TEntity : class =>
        (await set.Select(key).ToListAsync(ct)).ToHashSet();

    private static async Task<int> CopyAsync<TEntity>(
        IQueryable<TEntity> source, KnowledgeHubDbContext dest,
        CancellationToken ct, Func<TEntity, bool>? include = null)
        where TEntity : class
    {
        var rows = await source.AsNoTracking().ToListAsync(ct);
        if (include is not null)
            rows = rows.Where(include).ToList();
        if (rows.Count == 0)
            return 0;
        dest.AddRange(rows);
        await dest.SaveChangesAsync(ct);
        return rows.Count;
    }
}
