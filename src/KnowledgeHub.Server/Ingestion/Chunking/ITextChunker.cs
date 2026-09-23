namespace KnowledgeHub.Server.Ingestion.Chunking;

/// <summary>Document kind driving chunker selection (SPEC-20260923-code-aware-chunking).</summary>
public enum ChunkKind
{
    Markdown,
    Code,
    Config,
    Prose
}

/// <summary>One chunk of text plus its structural context (symbol path / config path).</summary>
public sealed record ChunkPiece(string Text, string? SymbolPath = null);

/// <summary>
/// Pluggable text chunker (SPEC-20260923-code-aware-chunking RF-001).
/// Implementations must be deterministic: same input ⇒ identical chunks.
/// </summary>
public interface ITextChunker
{
    ChunkKind Kind { get; }

    /// <summary>Splits <paramref name="text"/> into pieces ≤ roughly
    /// <paramref name="maxTokens"/> tokens (chars/4 approximation).</summary>
    IReadOnlyList<ChunkPiece> Chunk(string text, int maxTokens, int overlapTokens);
}
