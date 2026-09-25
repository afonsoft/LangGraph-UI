using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace KnowledgeHub.Server.Health;

/// <summary>
/// SPEC-20260925-redis-health-and-scan-stats RF-001: readiness covers the Redis
/// connection when the cache provider is redis. Fail-soft by design — the app
/// degrades gracefully without cache, so a dead Redis reports
/// <see cref="HealthStatus.Degraded"/> (visible) rather than Unhealthy
/// (which would take the pod out of rotation over a non-fatal dependency).
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var ping = await redis.GetDatabase().PingAsync();
            return ping.TotalMilliseconds > 500
                ? HealthCheckResult.Degraded($"redis ping {ping.TotalMilliseconds:F0}ms")
                : HealthCheckResult.Healthy($"redis ok ({ping.TotalMilliseconds:F0}ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"redis unreachable: {ex.Message}");
        }
    }
}
