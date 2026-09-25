namespace KnowledgeHub.Server.Eval;

/// <summary>Per-case outcome of an eval run (SPEC-20260923-eval-harness RF-004).</summary>
public sealed record EvalCaseResult
{
    public required string CaseId { get; init; }
    public required double Recall { get; init; }
    public required double Precision { get; init; }
    public required double ReciprocalRank { get; init; }
    /// <summary>Hit when any expected evidence was retrieved (or no-result for expectNoAnswer).</summary>
    public required bool Hit { get; init; }
    /// <summary>expectNoAnswer case that still returned results.</summary>
    public bool Inconsistent { get; init; }
    public double? Faithfulness { get; init; }
    /// <summary>Per-case failure — the run continues (spec edge case).</summary>
    public string? Error { get; init; }
    /// <summary>SPEC-20260924-eval-regression-gate RF-002: retrieval+generation
    /// wall time for this case.</summary>
    public double? LatencyMs { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Aggregate metrics of a run.</summary>
public sealed record EvalMetricsSummary
{
    public required double RecallAtK { get; init; }
    public required double PrecisionAtK { get; init; }
    public required double Mrr { get; init; }
    /// <summary>null when faithfulness was skipped (none configured / no provider).</summary>
    public double? Faithfulness { get; init; }
    public string? FaithfulnessSkippedReason { get; init; }
}

/// <summary>SPEC-20260924-eval-regression-gate RF-002: per-run latency percentiles.</summary>
public sealed record EvalLatencySummary
{
    public required double P50 { get; init; }
    public required double P95 { get; init; }
    public required double P99 { get; init; }
    public required double Mean { get; init; }
}

/// <summary>SPEC-20260924-eval-regression-gate RF-001: one pass/fail rule —
/// metric name (recall_at_k|precision_at_k|mrr|faithfulness|p50_ms|p95_ms|p99_ms|
/// mean_ms|duration_ms) + direction (gte|lte|gt|lt) + threshold.</summary>
public sealed record EvalGateRule
{
    public required string Metric { get; init; }
    public required string Direction { get; init; }
    public required double Threshold { get; init; }
}

/// <summary>Gate outcome persisted with the run.</summary>
public sealed record EvalGateResult
{
    /// <summary>pass | fail</summary>
    public required string Status { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
    /// <summary>Baseline run this gate compared against (when named).</summary>
    public string? BaselineName { get; init; }
}

/// <summary>Delta vs. a reference run (RF-005).</summary>
public sealed record EvalDelta
{
    public required Guid CompareRunId { get; init; }
    public required double RecallAtKDelta { get; init; }
    public required double PrecisionAtKDelta { get; init; }
    public required double MrrDelta { get; init; }
    /// <summary>Cases that were hits in the reference run and misses now.</summary>
    public required IReadOnlyList<string> Regressions { get; init; }
}

/// <summary>Full report returned by POST /api/eval/run and stored per run.</summary>
public sealed record EvalReport
{
    public required Guid RunId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required long DurationMs { get; init; }
    public required string DatasetHash { get; init; }
    public required int Cases { get; init; }
    public required EvalMetricsSummary Metrics { get; init; }
    public required IReadOnlyList<EvalCaseResult> Results { get; init; }
    public EvalDelta? Delta { get; init; }
    /// <summary>SPEC-20260924-eval-regression-gate RF-002: per-case latency percentiles.</summary>
    public EvalLatencySummary? Latency { get; init; }
    /// <summary>Gate outcome when the run carried gate rules.</summary>
    public EvalGateResult? Gate { get; init; }
    /// <summary>Named baseline this run compared/gated against.</summary>
    public string? BaselineName { get; init; }
}
