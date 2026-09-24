using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Server.Ingestion.Chunking;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260924-contextual-chunk-enrichment: heading-stack propagation and
/// structural prefix composition.
/// </summary>
public sealed class ContextualEnrichmentTests
{
    [Fact]
    public void Chunk_MarkdownPieces_CarrySectionPath()
    {
        var md = """
            # Guide
            intro text here that is long enough to matter for chunking purposes

            ## Install
            install instructions go here with enough words to form a body

            ## Config
            config details go here with enough words to form a body too
            """;

        // maxTokens 25 (~100 chars) forces one piece per section.
        var pieces = MarkdownTextChunker.For(ChunkKind.Markdown).Chunk(md, maxTokens: 25, overlapTokens: 5);

        Assert.True(pieces.Count >= 3);
        Assert.Equal("Guide", pieces[0].SectionPath);
        Assert.Contains(pieces, p => p.SectionPath == "Guide > Install");
        Assert.Contains(pieces, p => p.SectionPath == "Guide > Config");
        // Pieces continuing a section (no own header) inherit its path.
        var afterInstall = pieces.SkipWhile(p => p.SectionPath != "Guide > Install").ToList();
        Assert.All(afterInstall.Skip(1).TakeWhile(p => !p.Text.Contains("## Config")),
            p => Assert.Equal("Guide > Install", p.SectionPath));
    }

    [Fact]
    public void Chunk_NestedHeadings_PopToCorrectLevel()
    {
        var md = """
            # A
            body aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa

            ## B
            body bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb

            # C
            body cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc
            """;

        var pieces = MarkdownTextChunker.For(ChunkKind.Markdown).Chunk(md, 25, 5);

        var last = pieces.Last(p => p.Text.Contains("# C"));
        Assert.Equal("C", last.SectionPath); // h1 pops the h2 and h1
    }

    [Fact]
    public void Compose_Structural_BuildsPrefix()
    {
        var text = new string('x', 400); // 100 tokens — above MinTokens 40
        var enriched = ContextEnricher.Compose("vault", "Doc", "A > B", text, "structural", 40);

        Assert.NotNull(enriched);
        Assert.StartsWith("Source: vault | Document: Doc | Section: A > B", enriched);
        Assert.EndsWith(text, enriched);
    }

    [Fact]
    public void Compose_TinyChunk_ReturnsNull()
    {
        var enriched = ContextEnricher.Compose("s", "d", null, "tiny", "structural", 40);
        Assert.Null(enriched);
    }

    [Fact]
    public void Compose_Off_ReturnsNull()
    {
        var enriched = ContextEnricher.Compose("s", "d", null, new string('x', 400), "off", 40);
        Assert.Null(enriched);
    }

    [Fact]
    public void Compose_NoSection_OmitsSectionSegment()
    {
        var text = new string('x', 400);
        var enriched = ContextEnricher.Compose("vault", "Doc", null, text, "structural", 40);
        Assert.StartsWith("Source: vault | Document: Doc\n\n", enriched);
        Assert.DoesNotContain("Section:", enriched);
    }
}
