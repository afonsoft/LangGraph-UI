using KnowledgeHub.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Data;

/// <summary>
/// SPEC-20260926-unified-database-provider RF-005: one-shot catalog copy from the
/// legacy SQLite file to Postgres on the first postgres-mode boot. Runs only when
/// the sqlite file exists AND the postgres catalog is empty — preserving GUID
/// keys, so the already-migrated <c>kh_embeddings</c> rows stay joined to their
/// chunks. Marks the source file with a <c>.migrated</c> sidecar on success.
/// </summary>
public static class SqliteToPostgresMigrator
{
    public static async Task RunAsync(
        KnowledgeHubDbContext pg, IConfiguration cfg, ILogger logger, CancellationToken ct = default)
    {
        var path = DatabasePath.Resolve(cfg);
        if (!File.Exists(path))
            return;

        if (await pg.Sources.AnyAsync(ct))
        {
            logger.LogInformation("Postgres catalog already populated — skipping sqlite→postgres migration.");
            return;
        }

        var sqliteOptions = new DbContextOptionsBuilder<KnowledgeHubDbContext>()
            .UseSqlite($"Data Source={path};Mode=ReadOnly")
            .Options;
        await using var sqlite = new KnowledgeHubDbContext(sqliteOptions);

        if (!await sqlite.Sources.AnyAsync(ct))
        {
            logger.LogInformation("Sqlite catalog empty — nothing to migrate to Postgres.");
            return;
        }

        logger.LogInformation("Migrating sqlite catalog {Path} → Postgres (one-shot)…", path);

        // Parents before children — FK-safe order.
        var migrated = 0;
        migrated += await CopyAsync(sqlite.Sources, pg, ct);
        migrated += await CopyAsync(sqlite.Users, pg, ct);
        migrated += await CopyAsync(sqlite.ApiKeys, pg, ct);
        migrated += await CopyAsync(sqlite.Documents, pg, ct);
        migrated += await CopyAsync(sqlite.Chunks, pg, ct);
        migrated += await CopyAsync(sqlite.Threads, pg, ct);
        migrated += await CopyAsync(sqlite.ThreadMessages, pg, ct);
        migrated += await CopyAsync(sqlite.Approvals, pg, ct);
        migrated += await CopyAsync(sqlite.ApiKeyUsageEvents, pg, ct);
        migrated += await CopyAsync(sqlite.ApiKeyChatSettings, pg, ct);
        migrated += await CopyAsync(sqlite.IntegrationSecrets, pg, ct);
        migrated += await CopyAsync(sqlite.ChatSettings, pg, ct);
        migrated += await CopyAsync(sqlite.EmbeddingSettings, pg, ct);
        migrated += await CopyAsync(sqlite.GraphSettings, pg, ct);
        migrated += await CopyAsync(sqlite.EvalRuns, pg, ct);
        migrated += await CopyAsync(sqlite.EvalBaselines, pg, ct);
        migrated += await CopyAsync(sqlite.IngestionJobs, pg, ct);
        migrated += await CopyAsync(sqlite.KgNodes, pg, ct);
        migrated += await CopyAsync(sqlite.KgAliases, pg, ct);
        migrated += await CopyAsync(sqlite.KgEdges, pg, ct);
        migrated += await CopyAsync(sqlite.SecurityEvents, pg, ct);

        try { await File.WriteAllTextAsync(path + ".migrated", DateTimeOffset.UtcNow.ToString("O"), ct); }
        catch { /* marker is informational only */ }

        logger.LogInformation("Sqlite→Postgres catalog migration complete ({Rows} rows).", migrated);
    }

    private static async Task<int> CopyAsync<TEntity>(
        DbSet<TEntity> source, KnowledgeHubDbContext dest, CancellationToken ct)
        where TEntity : class
    {
        var rows = await source.AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0)
            return 0;
        dest.AddRange(rows);
        await dest.SaveChangesAsync(ct);
        return rows.Count;
    }
}
