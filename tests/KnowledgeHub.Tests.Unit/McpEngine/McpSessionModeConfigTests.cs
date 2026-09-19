using KnowledgeHub.McpEngine;
using ModelContextProtocol.AspNetCore;

namespace KnowledgeHub.Tests.Unit.McpEngine;

// Covers SPEC-20260918-mcp-v2-hybrid-transport RF-001: Mcp:SessionMode config knob
public class McpSessionModeConfigTests
{
    [Fact]
    public void ParseSessionMode_Null_DefaultsToHybrid()
    {
        // RF-001: default is StatefulForInitializeClients (hybrid)
        Assert.Equal(HttpServerSessionMode.StatefulForInitializeClients,
            McpServiceCollectionExtensions.ParseSessionMode(null));
    }

    [Fact]
    public void ParseSessionMode_Empty_DefaultsToHybrid()
    {
        Assert.Equal(HttpServerSessionMode.StatefulForInitializeClients,
            McpServiceCollectionExtensions.ParseSessionMode(""));
    }

    [Theory]
    [InlineData("Stateless", HttpServerSessionMode.Stateless)]
    [InlineData("Stateful", HttpServerSessionMode.Stateful)]
    [InlineData("StatefulForInitializeClients", HttpServerSessionMode.StatefulForInitializeClients)]
    [InlineData("stateless", HttpServerSessionMode.Stateless)] // case-insensitive
    public void ParseSessionMode_ValidValues_ReturnMode(string value, HttpServerSessionMode expected)
    {
        Assert.Equal(expected, McpServiceCollectionExtensions.ParseSessionMode(value));
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("0")] // Enum.TryParse accepts numerics — must not silently pick a mode
    public void ParseSessionMode_InvalidValue_ThrowsClearError(string value)
    {
        // Edge case: bogus config → startup fails with a message naming the key + valid values
        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServiceCollectionExtensions.ParseSessionMode(value));
        Assert.Contains("Mcp:SessionMode", ex.Message);
        Assert.Contains("StatefulForInitializeClients", ex.Message);
    }
}
