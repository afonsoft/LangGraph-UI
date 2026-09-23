namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>Persisted eval run (SPEC-20260923-eval-harness RF-004).</summary>
public sealed class EvalRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; }
    public long DurationMs { get; set; }
    /// <summary>SHA-256 of the dataset JSON — reproducibility key.</summary>
    public string DatasetHash { get; set; } = "";
    /// <summary>Serialized <see cref="Eval.EvalMetricsSummary"/>.</summary>
    public string MetricsJson { get; set; } = "";
    /// <summary>Serialized per-case <see cref="Eval.EvalCaseResult"/> list.</summary>
    public string PerCaseJson { get; set; } = "";
}
