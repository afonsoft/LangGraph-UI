namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// A directed relation between two <see cref="KgNode"/>s with mandatory
/// provenance — every edge traces to an evidence chunk, its document and
/// source (SPEC-20260923-graphrag RF-002: no provenance-free edges).
/// </summary>
public sealed class KgEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    /// <summary>Ontology kind: DEPENDS_ON|USES|PUBLISHED_IN|OWNED_BY|AFFECTED_BY|MENTIONS.</summary>
    public required string Kind { get; set; }
    /// <summary>The chunk the extractor cited as evidence — required.</summary>
    public Guid EvidenceChunkId { get; set; }
    public Guid KnowledgeDocumentId { get; set; }
    public Guid KnowledgeSourceId { get; set; }
    /// <summary>Extraction prompt version for future re-extraction (RF-008 guardrail).</summary>
    public string? PromptVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public KgNode From { get; set; } = null!;
    public KgNode To { get; set; } = null!;
    public KnowledgeDocument Document { get; set; } = null!;
}
