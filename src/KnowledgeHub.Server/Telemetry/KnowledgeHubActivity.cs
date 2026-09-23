using System.Diagnostics;

namespace KnowledgeHub.Server.Telemetry;

/// <summary>
/// Central <see cref="ActivitySource"/> (SPEC-20260923-observability-metrics RF-002).
/// With no listener attached <c>StartActivity</c> returns null — zero overhead.
/// Span attribute values follow <see cref="TelemetryTags"/>: no content, no PII.
/// </summary>
public static class KnowledgeHubActivity
{
    public static readonly ActivitySource Source = new(KnowledgeHubMetrics.MeterName, "0.1.0");

    /// <summary>Starts a span; returns null when nobody is listening.</summary>
    public static Activity? Start(string name) => Source.StartActivity(name, ActivityKind.Internal);

    /// <summary>Marks a span as failed and records the exception event.</summary>
    public static void Fail(Activity? activity, Exception ex)
    {
        if (activity is null)
            return;
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = ex.GetType().FullName,
            ["exception.message"] = ex.Message
        }));
    }
}
