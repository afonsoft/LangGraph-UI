using System.Collections.Concurrent;
using ModelContextProtocol.Server;

namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Tracks live <see cref="McpServer"/> sessions so catalog changes can broadcast
/// <c>notifications/tools/list_changed</c> (SPEC-04 RF-005). Servers self-register
/// via the incoming-message filter; the session middleware unregisters on close.
/// </summary>
public sealed class McpSessionRegistry
{
    private readonly ConcurrentDictionary<string, McpServer> _sessions = new();

    public void Register(McpServer server)
    {
        if (server.SessionId is { } id)
            _sessions[id] = server;
    }

    public void Unregister(string? sessionId)
    {
        if (sessionId is not null)
            _sessions.TryRemove(sessionId, out _);
    }

    public IReadOnlyList<McpServer> Active => [.. _sessions.Values];
}
