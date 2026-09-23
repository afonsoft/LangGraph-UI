using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Server.Ingestion.Chunking;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Ingestion.Chunking;

/// <summary>SPEC-20260923-code-aware-chunking RF-003 — config chunker ACs.</summary>
public sealed class ConfigTextChunkerTests
{
    [Fact]
    public void Json_TopLevelMembers_CarryPathPreamble()
    {
        var json = """
            {
              "server": { "host": "localhost", "port": 8080 },
              "logging": { "level": "info" }
            }
            """;
        var pieces = ConfigTextChunker.Instance.Chunk(json, maxTokens: 20, overlapTokens: 0);
        Assert.True(pieces.Count >= 2);
        Assert.All(pieces, p => Assert.StartsWith("$.", p.SymbolPath));
        Assert.Contains(pieces, p => p.Text.Contains("$.server"));
    }

    [Fact]
    public void Json_Malformed_FallsBackToProse_NoThrow()
    {
        var pieces = ConfigTextChunker.Instance.Chunk("{ not json !!!", 100, 10);
        Assert.Single(pieces);
        Assert.Contains("not json", pieces[0].Text);
    }

    [Fact]
    public void Yaml_TopLevelKeys_BecomeBlocks()
    {
        var yaml = """
            server:
              host: localhost
              port: 8080
            logging:
              level: info
            """;
        var pieces = ConfigTextChunker.Instance.Chunk(yaml, maxTokens: 12, overlapTokens: 0);
        Assert.True(pieces.Count >= 2);
        Assert.Contains(pieces, p => p.SymbolPath == "server");
        Assert.Contains(pieces, p => p.SymbolPath == "logging");
    }

    [Fact]
    public void Xml_TopLevelElements_BecomeBlocks()
    {
        var xml = """
            <configuration>
              <server host="localhost" port="8080" />
              <logging level="verbose-info-marker" />
            </configuration>
            """;
        var pieces = ConfigTextChunker.Instance.Chunk(xml, maxTokens: 15, overlapTokens: 0);
        Assert.True(pieces.Count >= 2);
        Assert.All(pieces, p => Assert.StartsWith("<configuration>", p.SymbolPath));
    }

    [Fact]
    public void Xml_Malformed_FallsBack()
    {
        var pieces = ConfigTextChunker.Instance.Chunk("<unclosed", 100, 10);
        Assert.NotEmpty(pieces);
    }

    [Fact]
    public void Markdown_Output_ByteIdentical()
    {
        // RF golden: MarkdownTextChunker must produce identical text to the
        // legacy static MarkdownChunker.
        var body = "# Title\n\npara one\n\n## Section\n\npara two\n\npara three";
        var legacy = MarkdownChunker.Chunk(body, 20, 5);
        var adapted = MarkdownTextChunker.Markdown.Chunk(body, 20, 5);
        Assert.Equal(legacy, adapted.Select(p => p.Text).ToList());
    }
}
