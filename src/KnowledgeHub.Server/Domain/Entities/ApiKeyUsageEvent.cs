namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// One audited request authenticated by an `aft_*` API key
/// (SPEC-20260915-apikey-usage-audit RF-002). Never stores bodies or secrets —
/// only method, path, status, duration and truncated user-agent. Retention:
/// 90 days or 10_000 events per key, pruned on insert.
/// </summary>
public sealed class ApiKeyUsageEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public required string HttpMethod { get; set; }
    public required string Path { get; set; }
    public int StatusCode { get; set; }
    public double DurationMs { get; set; }
    public string? UserAgent { get; set; }
}
