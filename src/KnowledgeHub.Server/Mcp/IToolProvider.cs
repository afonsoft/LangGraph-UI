namespace KnowledgeHub.Server.Mcp;

/// <summary>A family of dynamic MCP tools (SPEC-04 RF-001). Providers are singletons;
/// per-call services (DbContext, search) come from <paramref name="services"/>.</summary>
public interface IToolProvider
{
    Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken);
}
