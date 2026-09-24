using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Caching;

/// <summary>
/// Cache service for MCP and agent tool invocations (SPEC-20260924-redis-cache-and-tool-caching RF-002).
/// Caches read-only/idempotent tool results with a minimum TTL of 1 hour.
/// </summary>
public interface IToolCacheService
{
    bool IsCacheable(string toolName, bool isReadOnly);

    Task<CallToolResult?> GetCachedResultAsync(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken ct = default);

    Task SetCachedResultAsync(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        CallToolResult result,
        CancellationToken ct = default);
}
