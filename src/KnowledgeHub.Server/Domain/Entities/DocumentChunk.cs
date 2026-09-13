namespace KnowledgeHub.Server.Domain.Entities;

public sealed class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KnowledgeDocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public required string TextContent { get; set; }
    /// <summary>Little-endian IEEE-754 float32 blob (BLOB storage decision — compact, fast).</summary>
    public byte[]? Embedding { get; set; }
    /// <summary>Identity of the model that produced <see cref="Embedding"/>, e.g. "deterministic:hash384".</summary>
    public string? EmbeddingModel { get; set; }

    public KnowledgeDocument Document { get; set; } = null!;
}
