using System.Text.Json;
using KnowledgeHub.McpEngine.Activity;

namespace KnowledgeHub.Tests.Unit.McpEngine;

// Covers SPEC-20260914-mcp-contract-tests RF-002.
//
// Contract update rule: McpActivityEvent is the shape that flows through
// IMcpActivityFeed into the SignalR hub messages (SessionOpened /
// SessionClosed / Activity). A deliberate breaking change is fine — update
// the pinned values below in the same commit so the diff is reviewable.
public class ActivityEventContractTests
{
    [Fact]
    public void McpActivityKind_MemberNames_ArePinned()
    {
        Assert.Equal(
            ["SessionOpened", "SessionClosed", "Request", "ToolCall", "ApprovalRequested", "ApprovalResolved"],
            Enum.GetNames<McpActivityKind>());
    }

    [Fact]
    public void McpActivityEvent_PropertySet_IsPinned()
    {
        var names = typeof(McpActivityEvent).GetProperties()
            .Select(p => p.Name).Order().ToList();
        Assert.Equal(
            ["Caller", "DurationMs", "Error", "Kind", "Method", "SessionId", "Succeeded", "Timestamp", "ToolName", "Transport"],
            names);
    }

    [Fact]
    public void McpActivityEvent_CamelCaseWireShape_IsPinned()
    {
        var json = JsonSerializer.Serialize(new McpActivityEvent
        {
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Kind = McpActivityKind.ToolCall,
            SessionId = "s1",
            Transport = "http",
            Method = "tools/call",
            ToolName = "search_knowledge",
            DurationMs = 12.5,
            Succeeded = true,
            Error = null
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(
            ["caller", "durationMs", "error", "kind", "method", "sessionId", "succeeded", "timestamp", "toolName", "transport"],
            props);
    }
}
