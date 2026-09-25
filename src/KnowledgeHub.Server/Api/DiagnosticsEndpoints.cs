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
            Results.Ok(await VectorStoreDiagnostics.BuildAsync(vectors, cfg, db, ct)));

        return group;
    }
}
