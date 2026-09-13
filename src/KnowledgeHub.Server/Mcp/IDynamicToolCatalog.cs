namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// Aggregates all <see cref="IToolProvider"/>s into the live tool list.
/// Called on every <c>tools/list</c>/<c>tools/call</c> — never cached (SPEC-04 RF-001).
/// </summary>
public interface IDynamicToolCatalog
{
    Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken);
}
