using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KnowledgeHub.Server.Data;

/// <summary>
/// Postgres flavour of the catalog context — SPEC-20260926-unified-database-provider
/// RF-002. EF Core resolves migrations by the concrete context type annotated on
/// each migration class: Sqlite migrations annotate
/// <see cref="KnowledgeHubDbContext"/>; Postgres migrations annotate this subclass,
/// so both sets coexist in the same assembly without cross-application.
/// Registered via <c>AddDbContextPool&lt;KnowledgeHubDbContext,
/// PostgresKnowledgeHubDbContext&gt;</c> — consumers still inject the base type.
/// </summary>
public class PostgresKnowledgeHubDbContext(DbContextOptions<KnowledgeHubDbContext> options)
    : KnowledgeHubDbContext(options);

/// <summary>
/// The model differs per provider (e.g. <c>Embedding</c> = <c>bytea</c> vs
/// <c>BLOB</c>), so the pooled-model cache must key on the provider name in
/// addition to the context type.
/// </summary>
public sealed class ProviderAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), context.Database.ProviderName, designTime);
}
