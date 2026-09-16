using KnowledgeHub.McpEngine.Activity;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp;

/// <summary>Broadcasts <c>notifications/tools/list_changed</c> to connected sessions (SPEC-04 RF-005).
/// <see cref="Version"/> bumps on every change so the aggregated catalog can cache
/// and rebuild only when stale (SPEC-20260916-performance-memory-cache RF-001).</summary>
public interface IToolCatalogChangeNotifier
{
    /// <summary>Monotonic change counter — compare against the version a cached
    /// catalog was built at; rebuild when it differs.</summary>
    long Version { get; }

    Task NotifyToolsChangedAsync(CancellationToken cancellationToken = default);
}

public sealed class ToolCatalogChangeNotifier(
    McpSessionRegistry registry,
    ILogger<ToolCatalogChangeNotifier> logger) : IToolCatalogChangeNotifier
{
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public async Task NotifyToolsChangedAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _version);
        foreach (var session in registry.Active)
        {
            try
            {
                await session.SendNotificationAsync(
                    NotificationMethods.ToolListChangedNotification, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "tools/list_changed delivery failed for session {SessionId}", session.SessionId);
            }
        }
    }
}
