namespace KnowledgeHub.Server.Ingestion;

/// <summary>
/// SPEC-20260924-contextual-chunk-enrichment RF-001: deterministic structural
/// prefix ("Source | Document | Section") prepended to the text that is
/// embedded and FTS-indexed. The stored/displayed <c>Chunk.TextContent</c>
/// stays byte-identical — enrichment lives in <c>Chunk.EnrichedText</c>.
/// </summary>
public static class ContextEnricher
{
    /// <summary>Composes the enriched text for indexing. Returns null when
    /// enrichment is disabled or the chunk is below <paramref name="minTokens"/>
    /// (prefixes would dominate tiny chunks and dilute their embedding).</summary>
    public static string? Compose(
        string sourceName, string documentTitle, string? sectionPath,
        string chunkText, string mode, int minTokens)
    {
        if (!string.Equals(mode, "structural", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "llm", StringComparison.OrdinalIgnoreCase))
            return null;
        if (chunkText.Length / 4 < minTokens)
            return null;

        var header = $"Source: {sourceName} | Document: {documentTitle}" +
                     (sectionPath is not null ? $" | Section: {sectionPath}" : "");
        return header + "\n\n" + chunkText;
    }
}
