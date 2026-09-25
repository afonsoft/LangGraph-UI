namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// SPEC-20260924-eval-regression-gate RF-001: a named, approved reference run —
/// "golden" baseline that later runs compare/gate against. Unique by name.
/// </summary>
public sealed class EvalBaseline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Baseline name — e.g. "golden-v1".</summary>
    public required string Name { get; set; }
    /// <summary>The run promoted to baseline.</summary>
    public Guid EvalRunId { get; set; }
    /// <summary>Dataset fingerprint of the baseline run — runs on a different
    /// dataset must not compare against it.</summary>
    public required string DatasetHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public EvalRun? Run { get; set; }
}
