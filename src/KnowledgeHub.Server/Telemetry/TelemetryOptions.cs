namespace KnowledgeHub.Server.Telemetry;

/// <summary>
/// Exporter configuration (SPEC-20260923-observability-metrics RF-003).
/// Zero configuration = zero exporters; the <c>Meter</c>/<c>ActivitySource</c>
/// remain as no-op primitives.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>OTLP collector endpoint (e.g. <c>http://localhost:4317</c>). Empty = disabled.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Expose a Prometheus scrape endpoint at <c>/metrics</c> (Operational policy).</summary>
    public bool Prometheus { get; set; }

    /// <summary>Bind <c>Telemetry:*</c>.</summary>
    public static TelemetryOptions FromConfiguration(IConfiguration configuration) => new()
    {
        OtlpEndpoint = configuration.GetValue<string>("Telemetry:Otlp:Endpoint"),
        Prometheus = configuration.GetValue("Telemetry:Metrics:Prometheus", false)
    };
}
