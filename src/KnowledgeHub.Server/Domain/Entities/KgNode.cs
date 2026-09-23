namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// A knowledge-graph entity extracted from a chunk (SPEC-20260923-graphrag
/// RF-002). Identity is (NormalizedName, Type) — same normalized name with a
/// different type yields a distinct node plus <see cref="KgAlias"/> rows.
/// </summary>
public sealed class KgNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Display name as extracted (first-seen casing wins).</summary>
    public required string Name { get; set; }
    /// <summary>EntityResolver-normalized form (lower, trimmed, punctuation-collapsed).</summary>
    public required string NormalizedName { get; set; }
    /// <summary>Small ontology type: service|database|api|person|team|concept.</summary>
    public required string Type { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
