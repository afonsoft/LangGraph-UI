using KnowledgeHub.Server.Ingestion.Chunking;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Ingestion.Chunking;

/// <summary>SPEC-20260923-code-aware-chunking RF-002 — code chunker ACs.</summary>
public sealed class CodeTextChunkerTests
{
    private const string Sample = """
        using System;
        using System.Text;

        namespace Demo.App
        {
            public class Worker
            {
                public void Alpha()
                {
                    var a = 1;
                    Console.WriteLine(a);
                }

                public void Beta()
                {
                    var b = 2;
                    Console.WriteLine(b);
                }

                public void Gamma()
                {
                    var g = 3;
                    Console.WriteLine(g);
                }
            }
        }
        """;

    [Fact]
    public void MethodsStayIntact_WithSymbolPath()
    {
        var pieces = CodeTextChunker.Instance.Chunk(Sample, maxTokens: 200, overlapTokens: 20);

        // Each method name appears as a symbol path, and no method body is split.
        var paths = pieces.Select(p => p.SymbolPath).Where(p => p is not null).ToList();
        Assert.Contains(paths, p => p!.EndsWith("Alpha"));
        Assert.Contains(paths, p => p!.EndsWith("Beta"));
        Assert.Contains(paths, p => p!.EndsWith("Gamma"));
        Assert.All(paths, p => Assert.Contains("Demo.App.Worker", p));
    }

    [Fact]
    public void OversizedMember_GetsPartPreamble()
    {
        var big = new string('x', 2000);
        var code = $$"""
            namespace N;
            public class T
            {
                public void Big()
                {
                    {{big}}

                    {{big}}
                }
            }
            """;
        var pieces = CodeTextChunker.Instance.Chunk(code, maxTokens: 100, overlapTokens: 10);
        Assert.True(pieces.Count > 1);
        Assert.All(pieces.Skip(1), p => Assert.Contains("(part", p.Text));
    }

    [Fact]
    public void Selector_MapsExtensions()
    {
        Assert.Equal(ChunkKind.Code, ChunkerSelector.KindFor("a/b.cs"));
        Assert.Equal(ChunkKind.Code, ChunkerSelector.KindFor("script.py"));
        Assert.Equal(ChunkKind.Config, ChunkerSelector.KindFor("appsettings.json"));
        Assert.Equal(ChunkKind.Config, ChunkerSelector.KindFor("x.yaml"));
        Assert.Equal(ChunkKind.Markdown, ChunkerSelector.KindFor("note.md"));
        Assert.Equal(ChunkKind.Prose, ChunkerSelector.KindFor("Dockerfile"));
        Assert.Equal(ChunkKind.Prose, ChunkerSelector.KindFor(null));
    }
}
