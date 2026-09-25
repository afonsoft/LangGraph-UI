namespace KnowledgeHub.Server.Domain.Entities;

public sealed class KnowledgeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KnowledgeSourceId { get; set; }
    public required string Title { get; set; }
    /// <summary>Stable external reference (file path, URL, row key) — unique per source, used as upsert key.</summary>
    public required string UriReference { get; set; }
    /// <summary>SHA-256 hex of the raw content; unchanged docs are skipped on sync.</summary>
    public string? ContentHash { get; set; }
    /// <summary>SPEC-20260924-async-ingestion-queue RF-003: chunker version that
    /// produced this doc's chunks — a chunker change re-chunks even when the
    /// content hash matches.</summary>
    public int ChunkerVersion { get; set; }
    /// <summary>Hash of the chunking-relevant config (maxTokens/overlap/
    /// enrichment/strategy) used for this doc.</summary>
    public string? ChunkerConfigHash { get; set; }
    public string? RawContent { get; set; }
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;

    public KnowledgeSource Source { get; set; } = null!;
    public List<DocumentChunk> Chunks { get; set; } = [];
}
