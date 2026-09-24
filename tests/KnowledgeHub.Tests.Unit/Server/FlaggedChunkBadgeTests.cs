using KnowledgeHub.Server.Mcp.ToolProviders;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260923-flagged-chunk-badge — flagged chunks that survive exclusion
/// must surface their flags in every output shape (contract, citations, text).
/// </summary>
public sealed class FlaggedChunkBadgeTests
{
    private static SearchResultItem Hit(string? flags = null) =>
        new()
        {
            ChunkText = "ctx",
            DocumentTitle = "Doc",
            SourceName = "src",
            SourceId = Guid.NewGuid(),
            SourceType = SourceType.WebPage,
            Score = 0.9,
            UriReference = "uri-1",
            SuspicionFlags = flags
        };

    [Fact]
    public void SearchResultItem_SecurityFlagged_DerivesFromFlags()
    {
        Assert.False(Hit().SecurityFlagged);
        Assert.True(Hit("InstructionOverride").SecurityFlagged);
    }

    [Fact]
    public void FormatHits_MarksFlaggedChunksOnly()
    {
        var text = KnowledgeToolsProvider.FormatHits([Hit("InstructionOverride"), Hit()]);

        Assert.Contains("| flagged: InstructionOverride", text);
        Assert.Equal(1, text.Split("flagged:").Length - 1); // clean hit unmarked
    }

    [Fact]
    public void ExtractCitations_CarriesSuspicionFlags()
    {
        var citations = AnswerService.ExtractCitations("ans [1] [2]",
            [Hit("Jailbreak"), Hit()]);

        Assert.Equal("Jailbreak", citations[0].SuspicionFlags);
        Assert.Null(citations[1].SuspicionFlags);
    }
}
