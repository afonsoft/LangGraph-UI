using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Embeddings;

/// <summary>
/// SPEC-20260914-embedding-dimension-guard: at startup, compares persisted
/// embeddings (model + vector length) with the configured provider. Dimension
/// or model drift silently corrupts cosine search, so mismatches surface as
/// loud logs instead — non-fatal, never mutates data.
/// </summary>
public static class EmbeddingCompatibilityCheck
{
    public static async Task RunAsync(
        Data.KnowledgeHubDbContext db, IEmbeddingProvider provider, ILogger logger, CancellationToken cancellationToken = default)
    {
        var groups = await db.Database
            .SqlQuery<EmbeddingGroup>($"""
                SELECT "EmbeddingModel" AS "Model", LENGTH("Embedding") AS "Bytes", COUNT(*) AS "Count"
                FROM "Chunks"
                WHERE "Embedding" IS NOT NULL
                GROUP BY "EmbeddingModel", LENGTH("Embedding")
                """)
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
        {
            logger.LogInformation("Embedding store is empty — nothing to validate.");
            return;
        }

        var total = groups.Sum(g => g.Count);
        var dimMismatches = groups.Where(g => g.Bytes % sizeof(float) != 0 || g.Bytes / sizeof(float) != provider.Dimensions).ToList();
        var modelMismatches = groups.Where(g => g.Model != provider.ModelId).Select(g => g.Model).Distinct().ToList();

        foreach (var g in dimMismatches)
        {
            logger.LogError(
                "{Count} chunks are embedded with dims {Stored} but the active provider produces {Configured} (model {ModelId}). " +
                "Search results are unreliable — re-sync the affected sources or restore a matching Embeddings configuration.",
                g.Count, g.Bytes % sizeof(float) == 0 ? g.Bytes / sizeof(float) : -1, provider.Dimensions, provider.ModelId);
        }

        if (modelMismatches.Count > 0)
        {
            logger.LogWarning(
                "Chunks embedded with model(s) [{Models}] do not match the active provider model {ModelId} — " +
                "they are excluded from search until re-synced.",
                string.Join(", ", modelMismatches.Select(m => m ?? "<null>")), provider.ModelId);
        }

        if (dimMismatches.Count == 0 && modelMismatches.Count == 0)
        {
            logger.LogInformation("Embedding store OK ({Total} chunks, model {ModelId}, dims {Dimensions}).",
                total, provider.ModelId, provider.Dimensions);
        }
    }

    private sealed record EmbeddingGroup(string? Model, int Bytes, int Count);
}
