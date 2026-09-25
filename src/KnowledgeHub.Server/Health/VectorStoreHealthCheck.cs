using KnowledgeHub.Server.VectorStore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeHub.Server.Health;

/// <summary>
/// SPEC-20260925-vectorstore-metrics RF-003: vector store liveness under
/// "ready". A dead store degrades retrieval to FTS-only (the search pipeline
/// is fail-soft) — report <see cref="HealthStatus.Degraded"/>, never Unhealthy.
/// </summary>
public sealed class VectorStoreHealthCheck(
    IServiceScopeFactory scopeFactory, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var vectors = scope.ServiceProvider.GetRequiredService<IVectorStore>();
            // Zero-vector probe at the configured dimension: exercises
            // connectivity/schema without touching data.
            var dims = new float[configuration.GetValue("Embeddings:Dimensions", 384)];
            await vectors.SearchAsync(dims, "__health_probe__", topK: 1, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy($"{vectors.GetType().Name} ok");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"vector store unreachable: {ex.Message}");
        }
    }
}
