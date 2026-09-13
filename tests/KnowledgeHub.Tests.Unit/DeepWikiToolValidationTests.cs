using System.Text.Json;
using KnowledgeHub.Server.Mcp.Upstream;
using ModelContextProtocol;
using Xunit;

namespace KnowledgeHub.Tests.Unit;

public class DeepWikiToolValidationTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("\"langchain-ai/langgraph\"", "langchain-ai/langgraph")]
    [InlineData("\"a/b.c-d_e\"", "a/b.c-d_e")]
    public void ValidateRepoName_String_Ok(string json, string expected)
    {
        var repos = DeepWikiToolsProvider.ValidateRepoName(El(json));
        Assert.Equal([expected], repos);
    }

    [Fact]
    public void ValidateRepoName_Array_Ok()
    {
        var repos = DeepWikiToolsProvider.ValidateRepoName(El("""["a/b","c/d","e/f"]"""));
        Assert.Equal(3, repos.Count);
    }

    [Theory]
    [InlineData("\"nope\"")]
    [InlineData("\"a/b/c\"")]
    [InlineData("\"/repo\"")]
    [InlineData("\"owner/\"")]
    [InlineData("\"with space/repo\"")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void ValidateRepoName_Invalid_ThrowsInvalidParams(string json)
    {
        var ex = Assert.Throws<McpProtocolException>(() => DeepWikiToolsProvider.ValidateRepoName(El(json)));
        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public void ValidateRepoName_MoreThan10_Throws()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 11).Select(i => $"\"a{i}/b\"")) + "]";
        var ex = Assert.Throws<McpProtocolException>(() => DeepWikiToolsProvider.ValidateRepoName(El(json)));
        Assert.Equal(McpErrorCode.InvalidParams, ex.ErrorCode);
    }
}
