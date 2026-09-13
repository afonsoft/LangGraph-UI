using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>Unified semantic search over active sources (SPEC-02 RF-004).</summary>
public interface ISearchService
{
    /// <param name="sourceId">Restricts the search to one source; null searches all active sources.</param>
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int topK, Guid? sourceId = null, CancellationToken ct = default);
}
