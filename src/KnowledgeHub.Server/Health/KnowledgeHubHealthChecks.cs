using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeHub.Server.Health;

/// <summary>SPEC-20260914-health-checks: DB reachable and migrated (ready).</summary>
public sealed class DatabaseHealthCheck(KnowledgeHubDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database check failed", ex);
        }
    }
}

/// <summary>Embedding provider resolvable and reporting a model/dimensions.</summary>
public sealed class EmbeddingHealthCheck(IEmbeddingProvider provider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return provider.Dimensions > 0 && !string.IsNullOrWhiteSpace(provider.ModelId)
                ? Task.FromResult(HealthCheckResult.Healthy($"provider={provider.ModelId} dims={provider.Dimensions}"))
                : Task.FromResult(HealthCheckResult.Degraded("embedding provider misconfigured"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("embedding provider check failed", ex));
        }
    }
}

/// <summary>Ingestion service resolvable (watcher/sync pipeline wired).</summary>
public sealed class IngestionHealthCheck(IIngestionService ingestion) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ingestion is not null
            ? HealthCheckResult.Healthy("ingestion initialized")
            : HealthCheckResult.Unhealthy("ingestion service missing"));
}
