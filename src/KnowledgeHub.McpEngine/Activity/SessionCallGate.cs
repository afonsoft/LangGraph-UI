using System.Collections.Concurrent;

namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Per-session concurrency gate (SPEC-01 RF-004). Legacy SSE returns 202 before
/// handlers run, so there is no HTTP-level backpressure — this bounds in-flight
/// tool calls per session via <c>Mcp:MaxConcurrentCallsPerSession</c> (default 8).
/// Sessionless calls (hybrid transport serving 2026-07-28 clients) share a single
/// dedicated bucket with the same limit (SPEC-20260918 RF-004).
/// </summary>
public sealed class SessionCallGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly SemaphoreSlim _sessionless;
    private readonly int _maxConcurrentPerSession;

    public SessionCallGate(int maxConcurrentPerSession = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentPerSession, 1);
        _maxConcurrentPerSession = maxConcurrentPerSession;
        _sessionless = new SemaphoreSlim(maxConcurrentPerSession);
    }

    public async Task AcquireAsync(string? sessionId, CancellationToken cancellationToken) =>
        await GateFor(sessionId).WaitAsync(cancellationToken).ConfigureAwait(false);

    public void Release(string? sessionId)
    {
        var gate = GateFor(sessionId);
        if (gate.CurrentCount < _maxConcurrentPerSession)
            gate.Release();
    }

    // The sessionless bucket is a dedicated semaphore, not a dictionary key, so it
    // can never collide with a real Mcp-Session-Id value.
    private SemaphoreSlim GateFor(string? sessionId) =>
        sessionId is null ? _sessionless : _semaphores.GetOrAdd(sessionId, _ => new SemaphoreSlim(_maxConcurrentPerSession));
}
