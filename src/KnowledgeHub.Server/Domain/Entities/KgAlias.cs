namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Records entity-resolution decisions for auditability (SPEC-20260923-graphrag
/// RF-003): every merge (variant spelling → canonical node) and every
/// same-name-different-type conflict leaves an alias row.
/// </summary>
public sealed class KgAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Normalized spelling that resolved to the node.</summary>
    public required string AliasNormalized { get; set; }
    public Guid KgNodeId { get; set; }
    /// <summary>Source that produced the alias — null when created by a conflict record.</summary>
    public Guid? KnowledgeSourceId { get; set; }
    /// <summary>"merge" | "conflict" — conflicts mean a same-named node of
    /// another type also exists.</summary>
    public string Reason { get; set; } = "merge";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public KgNode Node { get; set; } = null!;
}
