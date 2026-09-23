namespace KnowledgeHub.Server.Ingestion.Chunking;

/// <summary>
/// SPEC-20260923-code-aware-chunking: adapter exposing <see cref="MarkdownChunker"/>
/// through <see cref="ITextChunker"/>. Output text is byte-identical to the
/// legacy chunker (regression guarantee) — SymbolPath is always null.
/// Also serves as the prose fallback.
/// </summary>
public sealed class MarkdownTextChunker : ITextChunker
{
    private MarkdownTextChunker(ChunkKind kind) => Kind = kind;

    public static readonly MarkdownTextChunker Markdown = new(ChunkKind.Markdown);
    public static readonly MarkdownTextChunker Prose = new(ChunkKind.Prose);

    public static MarkdownTextChunker For(ChunkKind kind) =>
        kind == ChunkKind.Prose ? Prose : Markdown;

    public ChunkKind Kind { get; }

    public IReadOnlyList<ChunkPiece> Chunk(string text, int maxTokens, int overlapTokens) =>
        MarkdownChunker.Chunk(text, maxTokens, overlapTokens)
            .Select(t => new ChunkPiece(t))
            .ToList();
}
