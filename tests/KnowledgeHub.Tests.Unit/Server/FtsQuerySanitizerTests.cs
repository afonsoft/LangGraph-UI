using KnowledgeHub.Server.Search;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260914-hybrid-retrieval NFR: FTS syntax never reaches the parser.
public class FtsQuerySanitizerTests
{
    [Fact]
    public void SimpleTerms_BecomeQuotedOr()
    {
        Assert.Equal("\"embeddings\" OR \"vetores\"", FtsQuerySanitizer.ToMatchExpression("embeddings vetores"));
    }

    [Fact]
    public void QuotesAndOperators_AreNeutralized()
    {
        // Unbalanced quote + FTS operators must become literal text.
        Assert.Equal("\"a\"\"b\" OR \"NEAR/2\" OR \"x*\"", FtsQuerySanitizer.ToMatchExpression("a\"b NEAR/2 x*"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_ReturnsNull(string? query)
    {
        Assert.Null(FtsQuerySanitizer.ToMatchExpression(query));
    }
}
