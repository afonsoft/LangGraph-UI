using KnowledgeHub.Server.Ingestion;

namespace KnowledgeHub.Tests.Unit.Ingestion;

// Covers SPEC-03 RF-001: frontmatter, tags, wikilinks, title extraction.
public class MarkdownNoteParserTests
{
    [Fact]
    public void Parse_ExtractsFrontmatterTags_AndStripsBlock()
    {
        var note = MarkdownNoteParser.Parse("""
            ---
            tags: [dotnet, ai]
            author: afonso
            ---
            # Minha Nota
            Conteúdo sobre vetores.
            """, "nota.md");

        Assert.Equal("Minha Nota", note.Title);
        Assert.Contains("dotnet", note.Tags);
        Assert.Contains("ai", note.Tags);
        Assert.Equal("afonso", note.Frontmatter["author"]);
        Assert.DoesNotContain("---", note.Body);
    }

    [Fact]
    public void Parse_CollectsWikiLinks_WithoutAliasOrAnchor()
    {
        var note = MarkdownNoteParser.Parse("# T\nVer [[Nota B|alias]] e [[pasta/Nota C#seção]]", "t.md");
        Assert.Contains("Nota B", note.WikiLinks);
        Assert.Contains("pasta/Nota C", note.WikiLinks);
    }

    [Fact]
    public void Parse_InlineTags_Captured()
    {
        var note = MarkdownNoteParser.Parse("# T\ntexto com #backend e #ai/ml", "t.md");
        Assert.Contains("backend", note.Tags);
        Assert.Contains("ai/ml", note.Tags);
    }

    [Fact]
    public void Parse_MalformedYaml_StillIndexesBody()
    {
        var note = MarkdownNoteParser.Parse("---\n: bad: :\n---\n# Título\nCorpo", "x.md");
        Assert.Equal("Corpo", note.Body.Trim().Split('\n').Last());
        Assert.Empty(note.Frontmatter);
    }

    [Fact]
    public void Parse_NoH1_FallsBackToFilename()
    {
        var note = MarkdownNoteParser.Parse("texto corrido sem header", "minha-nota.md");
        Assert.Equal("minha-nota", note.Title);
    }
}
