namespace KnowledgeHub.Server.Mcp;

/// <summary>
/// Aggregates all <see cref="IToolProvider"/>s into the live tool list.
/// Called on every <c>tools/list</c>/<c>tools/call</c> — never cached (SPEC-04 RF-001).
/// </summary>
public interface IDynamicToolCatalog
{
    /// <summary>Tools visible to the calling principal — scoped API keys see
    /// only their allowlisted subset (SPEC-20260923-source-authorization RF-004).</summary>
    Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken);

    /// <summary>The full aggregate, unfiltered — used by call dispatchers to
    /// distinguish "unknown tool" from "hidden by key scope".</summary>
    Task<IReadOnlyList<CatalogTool>> GetUnfilteredToolsAsync(IServiceProvider services, CancellationToken cancellationToken);
}
