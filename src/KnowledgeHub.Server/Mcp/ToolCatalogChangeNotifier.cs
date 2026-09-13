using KnowledgeHub.McpEngine.Activity;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp;

/// <summary>Broadcasts <c>notifications/tools/list_changed</c> to connected sessions (SPEC-04 RF-005).</summary>
public interface IToolCatalogChangeNotifier
{
    Task NotifyToolsChangedAsync(CancellationToken cancellationToken = default);
}

public sealed class ToolCatalogChangeNotifier(
    McpSessionRegistry registry,
    ILogger<ToolCatalogChangeNotifier> logger) : IToolCatalogChangeNotifier
{
    public async Task NotifyToolsChangedAsync(CancellationToken cancellationToken = default)
    {
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
