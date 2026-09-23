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
    /// <summary>SPEC-20260923-code-aware-chunking RF-004: chunker that produced
    /// this chunk — markdown|code|config|prose.</summary>
    public string ChunkKind { get; set; } = "markdown";
    /// <summary>Structural context — e.g. <c>Namespace.Type.Method</c> or
    /// <c>$.server.port</c>; null for prose/markdown chunks.</summary>
    public string? SymbolPath { get; set; }

    public KnowledgeDocument Document { get; set; } = null!;
}
