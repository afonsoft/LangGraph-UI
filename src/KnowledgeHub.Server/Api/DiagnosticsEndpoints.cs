using KnowledgeHub.Server.VectorStore;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// SPEC-20260925-pgvector-source-cascade RF-004: diagnostics endpoints —
/// vector store provider/size/index status for ops inspection.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static RouteGroupBuilder MapDiagnosticsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/diagnostics");

        group.MapGet("/vectorstore", async (
            IVectorStore vectors, IConfiguration cfg,
            Data.KnowledgeHubDbContext db, CancellationToken ct) =>
        {
            var provider = cfg.GetValue("VectorStore:Provider", "sqlite");
            if (vectors is PostgresVectorStore pg)
                return Results.Ok(await pg.GetDiagnosticsAsync(ct));

            // sqlite providers: embeddings live in DocumentChunks / vec_chunks —
            // count embedded chunks from the catalog db.
            var embedded = await db.Chunks.CountAsync(c => c.Embedding != null, ct);
            return Results.Ok(new
            {
                provider,
                rows = embedded,
                size = (string?)null,
                hnswIndex = provider.Equals("sqlite-vec", StringComparison.OrdinalIgnoreCase),
                pgvectorVersion = (string?)null,
                dimensions = cfg.GetValue("Embeddings:Dimensions", 384)
            });
        });

        return group;
    }
}
