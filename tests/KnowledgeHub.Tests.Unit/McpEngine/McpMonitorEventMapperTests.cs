using System.Text.Json;
using KnowledgeHub.McpEngine.Activity;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Tests.Unit.McpEngine;

// Covers SPEC-20260915-mcp-monitor-activity RF-001/RF-002: the mapper is the
// single projection shared by the hub snapshot and the live broadcast — its
// output shape is pinned here so the two channels cannot diverge again.
public class McpMonitorEventMapperTests
{
    [Fact]
    public void Map_ToolCall_FormatsMethodAndDetail()
    {
        var dto = McpMonitorEventMapper.Map(new McpActivityEvent
        {
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Kind = McpActivityKind.ToolCall,
            SessionId = "s1",
            Transport = "streamable-http",
            Method = "tools/call",
            ToolName = "search_knowledge",
            DurationMs = 12.5,
            Succeeded = true
        });

        Assert.Equal("ToolCall", dto.Kind);
        Assert.Equal("tools/call:search_knowledge", dto.Method);
        Assert.Equal("streamable-http", dto.Detail);
        Assert.Equal(12.5, dto.DurationMs);
        Assert.True(dto.Succeeded);
    }

    [Fact]
    public void Map_Failure_PrefersErrorOverTransport()
    {
        var dto = McpMonitorEventMapper.Map(new McpActivityEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = McpActivityKind.Request,
            SessionId = "s1",
            Transport = "sse",
            Method = "tools/list",
            Succeeded = false,
            Error = "boom"
        });

        Assert.Equal("boom", dto.Detail);
        Assert.False(dto.Succeeded);
    }

    [Fact]
    public void Map_SessionLifecycle_CarriesKindAndSessionId()
    {
        var dto = McpMonitorEventMapper.Map(new McpActivityEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = McpActivityKind.SessionOpened,
            SessionId = "abc",
            Transport = "streamable-http"
        });

        Assert.Equal(McpMonitorEventKind.SessionOpened, dto.Kind);
        Assert.Equal("abc", dto.SessionId);
        Assert.Null(dto.Method);
    }

    [Fact]
    public void Dto_CamelCaseWireShape_IsPinned()
    {
        var json = JsonSerializer.Serialize(new McpMonitorEventDto
        {
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Kind = "ToolCall",
            SessionId = "s1",
            Method = "tools/call:search_knowledge",
            Detail = "streamable-http",
            DurationMs = 12.5,
            Succeeded = true
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(
            ["detail", "durationMs", "kind", "method", "sessionId", "succeeded", "timestamp"],
            props);
    }
}
