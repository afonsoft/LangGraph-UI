using KnowledgeHub.McpEngine.Activity;

namespace KnowledgeHub.Tests.Unit.McpEngine;

// Covers RF-004: per-session concurrency gate
public class SessionCallGateTests
{
    [Fact]
    public async Task AcquireAsync_BlocksBeyondLimit_PerSession()
    {
        var gate = new SessionCallGate(maxConcurrentPerSession: 2);
        await gate.AcquireAsync("s1", CancellationToken.None);
        await gate.AcquireAsync("s1", CancellationToken.None);

        var third = gate.AcquireAsync("s1", CancellationToken.None);
        Assert.False(third.IsCompleted);

        // a different session is unaffected
        await gate.AcquireAsync("s2", CancellationToken.None);
        gate.Release("s2");

        gate.Release("s1");
        await third; // now unblocked
        gate.Release("s1");
        gate.Release("s1");
    }

    [Fact]
    public async Task Release_WithoutAcquire_IsNoOp()
    {
        var gate = new SessionCallGate(1);
        gate.Release("never-acquired"); // must not throw
        await gate.AcquireAsync("never-acquired", CancellationToken.None);
    }
}
