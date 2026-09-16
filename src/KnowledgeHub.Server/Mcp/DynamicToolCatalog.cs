namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// Catalog aggregator — last-registered provider wins on name collisions.
/// The aggregate is cached per <see cref="IToolCatalogChangeNotifier.Version"/>
/// (SPEC-20260916-performance-memory-cache RF-001): sources, secrets and
/// upstream sessions all invalidate through the notifier, so every consumer
/// (tools/list, tools/call, agent loop, Playground) shares one build instead of
/// re-running every provider per request. Cached <see cref="CatalogTool"/>s are
/// safe to share — handlers resolve scoped services from the call-time
/// <see cref="ToolCallContext.Services"/>.
/// </summary>
public sealed class DynamicToolCatalog(
    IEnumerable<IToolProvider> providers,
    IToolCatalogChangeNotifier notifier) : IDynamicToolCatalog
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<CatalogTool>? _cached;
    private long _cachedVersion = -1;

    public async Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var version = notifier.Version;
        if (_cached is { } hit && _cachedVersion == version)
            return hit;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            version = notifier.Version; // re-read under the gate
            if (_cached is { } again && _cachedVersion == version)
                return again;

            var tools = new Dictionary<string, CatalogTool>(StringComparer.Ordinal);
            foreach (var provider in providers)
                foreach (var tool in await provider.GetToolsAsync(services, cancellationToken))
                    tools[tool.Name] = tool;

            _cached = [.. tools.Values];
            _cachedVersion = version;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
