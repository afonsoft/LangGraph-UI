namespace KnowledgeHub.Server.Mcp;

/// <summary>Catalog aggregator — last-registered provider wins on name collisions.</summary>
public sealed class DynamicToolCatalog(IEnumerable<IToolProvider> providers) : IDynamicToolCatalog
{
    public async Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tools = new Dictionary<string, CatalogTool>(StringComparer.Ordinal);
        foreach (var provider in providers)
            foreach (var tool in await provider.GetToolsAsync(services, cancellationToken))
                tools[tool.Name] = tool;
        return [.. tools.Values];
    }
}
