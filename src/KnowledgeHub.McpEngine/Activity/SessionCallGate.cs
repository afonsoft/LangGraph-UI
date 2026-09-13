using System.Collections.Concurrent;

namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Per-session concurrency gate (SPEC-01 RF-004). Legacy SSE returns 202 before
/// handlers run, so there is no HTTP-level backpressure — this bounds in-flight
/// tool calls per session via <c>Mcp:MaxConcurrentCallsPerSession</c> (default 8).
/// </summary>
public sealed class SessionCallGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly int _maxConcurrentPerSession;

    public SessionCallGate(int maxConcurrentPerSession = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentPerSession, 1);
        _maxConcurrentPerSession = maxConcurrentPerSession;
    }

    public async Task AcquireAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var gate = GateFor(sessionId);
        if (gate is not null)
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release(string? sessionId)
    {
        var gate = GateFor(sessionId);
        if (gate is not null && gate.CurrentCount < _maxConcurrentPerSession)
            gate.Release();
    }

    private SemaphoreSlim? GateFor(string? sessionId) =>
        sessionId is null ? null : _semaphores.GetOrAdd(sessionId, _ => new SemaphoreSlim(_maxConcurrentPerSession));
}
