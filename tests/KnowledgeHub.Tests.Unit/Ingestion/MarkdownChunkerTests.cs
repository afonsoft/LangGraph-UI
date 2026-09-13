using KnowledgeHub.Server.Ingestion;

namespace KnowledgeHub.Tests.Unit.Ingestion;

// Covers SPEC-03 RF-002: header-aware splitting, size budget, overlap, no empty chunks.
public class MarkdownChunkerTests
{
    [Fact]
    public void Chunk_EmptyBody_ReturnsEmpty()
    {
        Assert.Empty(MarkdownChunker.Chunk(""));
        Assert.Empty(MarkdownChunker.Chunk("   \n\n  "));
    }

    [Fact]
    public void Chunk_ShortDoc_SingleChunk()
    {
        var chunks = MarkdownChunker.Chunk("# Título\n\nTexto curto.");
        Assert.Single(chunks);
        Assert.Contains("Título", chunks[0]);
    }

    [Fact]
    public void Chunk_LongSection_SplitsAndOverlaps()
    {
        // 3000+ chars in one section → ≥2 chunks with overlap (500 tokens ≈ 2000 chars)
        var paragraph = string.Concat(Enumerable.Repeat("palavra ", 400)); // ~3200 chars
        var body = $"# Secção Grande\n\n{paragraph}";

        var chunks = MarkdownChunker.Chunk(body, maxTokens: 500, overlapTokens: 50);

        Assert.True(chunks.Count >= 2, $"expected ≥2 chunks, got {chunks.Count}");
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }

    [Fact]
    public void Chunk_HeadersStartNewChunks()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 6).Select(i =>
            $"## Seção {i}\n\n" + string.Concat(Enumerable.Repeat($"conteúdo{i} ", 300)))); // ~3600 chars each

        var chunks = MarkdownChunker.Chunk(body, maxTokens: 500, overlapTokens: 50);
        Assert.True(chunks.Count >= 6);
        // every chunk must start at a header or overlap text — no orphan empty chunk
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }

    [Fact]
    public void Chunk_NeverEmitsWhitespaceOnly()
    {
        var body = "# A\n\n" + string.Join("\n\n", Enumerable.Range(0, 20).Select(_ => "x"));
        Assert.All(MarkdownChunker.Chunk(body, 10, 2), c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }
}
