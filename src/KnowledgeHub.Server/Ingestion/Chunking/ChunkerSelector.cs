using System.Collections.Generic;

namespace KnowledgeHub.Server.Ingestion.Chunking;

/// <summary>
/// SPEC-20260923-code-aware-chunking RF-001: deterministic mapping
/// document extension → <see cref="ChunkKind"/> → chunker instance.
/// Unknown/extensionless kinds fall back to prose (markdown behavior).
/// </summary>
public static class ChunkerSelector
{
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".java", ".js", ".ts", ".jsx", ".tsx", ".py", ".go", ".rs",
        ".c", ".h", ".cpp", ".hpp", ".csx", ".fs", ".rb", ".php", ".swift",
        ".kt", ".kts", ".scala", ".sh", ".ps1", ".sql"
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml", ".xml", ".toml", ".ini", ".config",
        ".csproj", ".props", ".targets", ".slnx"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".markdown", ".mdx" };

    /// <summary>Classifies a document by its URI/path extension.</summary>
    public static ChunkKind KindFor(string? uriOrPath)
    {
        var ext = Path.GetExtension(uriOrPath ?? "");
        if (MarkdownExtensions.Contains(ext)) return ChunkKind.Markdown;
        if (CodeExtensions.Contains(ext)) return ChunkKind.Code;
        if (ConfigExtensions.Contains(ext)) return ChunkKind.Config;
        return ChunkKind.Prose;
    }

    /// <summary>Resolves the chunker for a kind (prose falls back to markdown rules).</summary>
    public static ITextChunker For(ChunkKind kind) => kind switch
    {
        ChunkKind.Code => CodeTextChunker.Instance,
        ChunkKind.Config => ConfigTextChunker.Instance,
        _ => MarkdownTextChunker.For(kind)
    };

    /// <summary>Convenience: classify + chunk in one call.</summary>
    public static (ChunkKind Kind, IReadOnlyList<ChunkPiece> Pieces) Chunk(
        string? uriOrPath, string text, int maxTokens, int overlapTokens)
    {
        var kind = KindFor(uriOrPath);
        return (kind, For(kind).Chunk(text, maxTokens, overlapTokens));
    }
}
