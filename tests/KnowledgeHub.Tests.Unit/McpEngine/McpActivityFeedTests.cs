using KnowledgeHub.McpEngine.Activity;

namespace KnowledgeHub.Tests.Unit.McpEngine;

// Covers RF-003: activity feed ring buffer + pub/sub, never throws into pipeline
public class McpActivityFeedTests
{
    private static McpActivityEvent Evt(string? sessionId = "s1") =>
        new() { Timestamp = DateTimeOffset.UtcNow, Kind = McpActivityKind.Request, SessionId = sessionId, Method = "tools/list" };

    [Fact]
    public void Record_AppendsEvent_AndSnapshotReturnsIt()
    {
        var feed = new McpActivityFeed(10);
        feed.Record(Evt());
        var snap = feed.Snapshot();
        Assert.Single(snap);
        Assert.Equal("tools/list", snap[0].Method);
    }

    [Fact]
    public void Record_EvictsOldest_WhenCapacityExceeded()
    {
        var feed = new McpActivityFeed(3);
        for (var i = 0; i < 5; i++)
            feed.Record(new McpActivityEvent { Timestamp = DateTimeOffset.UtcNow, Kind = McpActivityKind.Request, Method = $"m{i}" });
        var methods = feed.Snapshot().Select(e => e.Method).ToArray();
        Assert.Equal(new[] { "m2", "m3", "m4" }, methods);
    }

    [Fact]
    public void Record_PublishesToSubscribers()
    {
        var feed = new McpActivityFeed(10);
        McpActivityEvent? received = null;
        feed.Published += e => received = e;
        feed.Record(Evt());
        Assert.NotNull(received);
        Assert.Equal("tools/list", received!.Method);
    }

    [Fact]
    public void Record_SwallowsSubscriberExceptions()
    {
        var feed = new McpActivityFeed(10);
        feed.Published += _ => throw new InvalidOperationException("boom");
        var ex = Record.Exception(() => feed.Record(Evt()));
        Assert.Null(ex);
        Assert.Single(feed.Snapshot());
    }

    [Fact]
    public void Snapshot_IsThreadSafe_UnderConcurrentWrites()
    {
        var feed = new McpActivityFeed(100);
        Parallel.For(0, 500, i => feed.Record(new McpActivityEvent { Timestamp = DateTimeOffset.UtcNow, Kind = McpActivityKind.Request, Method = $"m{i}" }));
        Assert.Equal(100, feed.Snapshot().Count);
    }
}
