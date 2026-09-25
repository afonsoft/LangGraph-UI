using KnowledgeHub.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.VectorStore;

/// <summary>
/// SPEC-20260926-settings-tabs-database-metrics RF-002: shared builder for the
/// vector-store diagnostics payload — used by GET /api/diagnostics/vectorstore
/// and GET /api/settings/database so both surfaces report the same numbers.
/// </summary>
public static class VectorStoreDiagnostics
{
    public static async Task<object> BuildAsync(
        IVectorStore vectors, IConfiguration cfg,
        KnowledgeHubDbContext db, CancellationToken ct)
    {
        var provider = cfg.GetValue("VectorStore:Provider", "sqlite");
        if (vectors is PostgresVectorStore pg)
            return await pg.GetDiagnosticsAsync(ct);

        // sqlite providers: embeddings live in DocumentChunks / vec_chunks —
        // count embedded chunks from the catalog db.
        var embedded = await db.Chunks.CountAsync(c => c.Embedding != null, ct);
        return new
        {
            provider,
            rows = embedded,
            size = (string?)null,
            hnswIndex = provider.Equals("sqlite-vec", StringComparison.OrdinalIgnoreCase),
            pgvectorVersion = (string?)null,
            dimensions = cfg.GetValue("Embeddings:Dimensions", 384)
        };
    }
}
