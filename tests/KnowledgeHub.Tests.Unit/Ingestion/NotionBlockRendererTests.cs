using System.Text.Json;
using KnowledgeHub.Server.Ingestion.Connectors;

namespace KnowledgeHub.Tests.Unit.Ingestion;

// Covers SPEC-20260919-notion-connector RF-004 (block→text flattening, bounds)
// and RF-005 (property serialization for database rows).
public class NotionBlockRendererTests
{
    private static List<NotionBlock> Blocks(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new NotionBlock(e.Clone(), []))
            .ToList();
    }

    private static NotionBlock Block(string json, params NotionBlock[] children)
    {
        using var doc = JsonDocument.Parse(json);
        return new NotionBlock(doc.RootElement.Clone(), children);
    }

    [Fact]
    public void Paragraph_RendersPlainText()
    {
        var blocks = Blocks("""
            [{"type":"paragraph","paragraph":{"rich_text":[{"plain_text":"Hello world"}]}}]
            """);

        Assert.Equal("Hello world", NotionBlockRenderer.Render(blocks));
    }

    [Fact]
    public void RichText_ConcatenatesFragments_IgnoresAnnotations()
    {
        var blocks = Blocks("""
            [{"type":"paragraph","paragraph":{"rich_text":[
                {"plain_text":"bold ","annotations":{"bold":true}},
                {"plain_text":"and plain"}]}}]
            """);

        Assert.Equal("bold and plain", NotionBlockRenderer.Render(blocks));
    }

    [Theory]
    [InlineData("heading_1", "# Title")]
    [InlineData("heading_2", "## Title")]
    [InlineData("heading_3", "### Title")]
    public void Headings_MapToMarkdownLevels(string type, string expected)
    {
        var blocks = Blocks($$$"""
            [{"type":"{{{type}}}","{{{type}}}":{"rich_text":[{"plain_text":"Title"}]}}]
            """);

        Assert.Equal(expected, NotionBlockRenderer.Render(blocks));
    }

    [Fact]
    public void ListItems_RenderDashAndNumber()
    {
        var blocks = Blocks("""
            [
              {"type":"bulleted_list_item","bulleted_list_item":{"rich_text":[{"plain_text":"item a"}]}},
              {"type":"numbered_list_item","numbered_list_item":{"rich_text":[{"plain_text":"item b"}]}}
            ]
            """);

        var text = NotionBlockRenderer.Render(blocks);

        Assert.Contains("- item a", text);
        Assert.Contains("1. item b", text);
    }

    [Theory]
    [InlineData(false, "- [ ] task")]
    [InlineData(true, "- [x] task")]
    public void ToDo_RendersCheckbox(bool done, string expected)
    {
        var blocks = Blocks($$$"""
            [{"type":"to_do","to_do":{"checked":{{{done.ToString().ToLower()}}},"rich_text":[{"plain_text":"task"}]}}]
            """);

        Assert.Equal(expected, NotionBlockRenderer.Render(blocks));
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("callout")]
    public void QuoteAndCallout_PrefixGreaterThan(string type)
    {
        var blocks = Blocks($$$"""
            [{"type":"{{{type}}}","{{{type}}}":{"rich_text":[{"plain_text":"wisdom"}]}}]
            """);

        Assert.Equal("> wisdom", NotionBlockRenderer.Render(blocks));
    }

    [Fact]
    public void Code_RendersFencedBlock()
    {
        var blocks = Blocks("""
            [{"type":"code","code":{"language":"csharp","rich_text":[{"plain_text":"var x = 1;"}]}}]
            """);

        var text = NotionBlockRenderer.Render(blocks);

        Assert.Contains("```csharp", text);
        Assert.Contains("var x = 1;", text);
        Assert.EndsWith("```", text);
    }

    [Fact]
    public void Divider_RendersHorizontalRule()
    {
        var blocks = Blocks("""[{"type":"divider","divider":{}}]""");

        Assert.Equal("---", NotionBlockRenderer.Render(blocks));
    }

    [Fact]
    public void Table_RendersPipeRows()
    {
        var table = Block("""{"type":"table","table":{"table_width":2}}""",
            Block("""{"type":"table_row","table_row":{"cells":[[{"plain_text":"a"}],[{"plain_text":"b"}]]}}"""),
            Block("""{"type":"table_row","table_row":{"cells":[[{"plain_text":"c"}],[{"plain_text":"d"}]]}}"""));

        var text = NotionBlockRenderer.Render([table]);

        Assert.Contains("a | b", text);
        Assert.Contains("c | d", text);
    }

    [Fact]
    public void ChildPage_RendersMarker()
    {
        var blocks = Blocks("""
            [{"type":"child_page","id":"child-1","child_page":{"title":"Sub Page"}}]
            """);

        Assert.Equal("[página: Sub Page]", NotionBlockRenderer.Render(blocks));
    }

    [Fact]
    public void ChildDatabase_RendersMarker()
    {
        var blocks = Blocks("""
            [{"type":"child_database","id":"db-1","child_database":{"title":"My DB"}}]
            """);

        Assert.Equal("[database: My DB]", NotionBlockRenderer.Render(blocks));
    }

    [Theory]
    [InlineData("image", "image", "external", "https://x/pic.png", "[imagem: https://x/pic.png]")]
    [InlineData("file", "file", "file", "https://x/doc.pdf", "[arquivo: https://x/doc.pdf]")]
    [InlineData("video", "video", "external", "https://x/v.mp4", "[video: https://x/v.mp4]")]
    [InlineData("bookmark", "bookmark", null, "https://x/page", "[bookmark: https://x/page]")]
    public void MediaBlocks_RenderNote(string type, string payloadKey, string? urlKind, string url, string expected)
    {
        var payload = urlKind is null
            ? $"{{\"url\":\"{url}\"}}"
            : $"{{\"{urlKind}\":{{\"url\":\"{url}\"}}}}";
        var blocks = Blocks($$$"""[{"type":"{{{type}}}","{{{payloadKey}}}":{{{payload}}},"caption":[]}]""");

        Assert.Equal(expected, NotionBlockRenderer.Render(blocks));
    }

    [Fact]
    public void UnknownType_SkippedSilently()
    {
        var blocks = Blocks("""
            [
              {"type":"paragraph","paragraph":{"rich_text":[{"plain_text":"keep"}]}},
              {"type":"unsupported_new_thing","unsupported_new_thing":{}}
            ]
            """);

        var text = NotionBlockRenderer.Render(blocks);

        Assert.Equal("keep", text);
    }

    [Fact]
    public void NestedChildren_IndentedUnderParent()
    {
        var parent = Block("""{"type":"bulleted_list_item","bulleted_list_item":{"rich_text":[{"plain_text":"parent"}]}}""",
            Block("""{"type":"bulleted_list_item","bulleted_list_item":{"rich_text":[{"plain_text":"child"}]}}"""));

        var text = NotionBlockRenderer.Render([parent]);

        Assert.Contains("- parent", text);
        Assert.Contains("  - child", text);
    }

    [Fact]
    public void PageTitle_ExtractedFromTitleProperty()
    {
        using var doc = JsonDocument.Parse("""
            {"properties":{"Name":{"type":"title","title":[{"plain_text":"My Page"}]},
                            "Other":{"type":"rich_text","rich_text":[{"plain_text":"x"}]}}}
            """);

        Assert.Equal("My Page", NotionBlockRenderer.ExtractPageTitle(doc.RootElement));
    }

    [Fact]
    public void PageTitle_FallsBackToId()
    {
        using var doc = JsonDocument.Parse("""{"id":"abc-123","properties":{}}""");

        Assert.Equal("abc-123", NotionBlockRenderer.ExtractPageTitle(doc.RootElement));
    }

    [Fact]
    public void SerializeProperties_CommonTypes()
    {
        using var doc = JsonDocument.Parse("""
            {
              "Name":    {"type":"title","title":[{"plain_text":"Row 1"}]},
              "Notes":   {"type":"rich_text","rich_text":[{"plain_text":"some note"}]},
              "Count":   {"type":"number","number":42},
              "Status":  {"type":"select","select":{"name":"Done"}},
              "Tags":    {"type":"multi_select","multi_select":[{"name":"a"},{"name":"b"}]},
              "Due":     {"type":"date","date":{"start":"2026-09-20","end":null}},
              "Done":    {"type":"checkbox","checkbox":true},
              "Link":    {"type":"url","url":"https://x"},
              "Mail":    {"type":"email","email":"a@b.c"},
              "Owner":   {"type":"people","people":[{"name":"Afonso"}]},
              "Rel":     {"type":"relation","relation":[{"id":"r-1"}]},
              "Calc":    {"type":"formula","formula":{"type":"number","number":7}},
              "Weird":   {"type":"rollup","rollup":{"type":"array","array":[]}}
            }
            """);

        var lines = NotionBlockRenderer.SerializeProperties(doc.RootElement);

        Assert.Contains("Name: Row 1", lines);
        Assert.Contains("Notes: some note", lines);
        Assert.Contains("Count: 42", lines);
        Assert.Contains("Status: Done", lines);
        Assert.Contains("Tags: a, b", lines);
        Assert.Contains("Due: 2026-09-20", lines);
        Assert.Contains("Done: True", lines);
        Assert.Contains("Link: https://x", lines);
        Assert.Contains("Mail: a@b.c", lines);
        Assert.Contains("Owner: Afonso", lines);
        Assert.Contains("Rel: r-1", lines);
        Assert.Contains("Calc: 7", lines);
        Assert.Contains("Weird: (unsupported)", lines);
    }
}
