using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>Unified search over active sources (SPEC-02 RF-004, SPEC-20260914-hybrid-retrieval).</summary>
public interface ISearchService
{
    /// <param name="sourceId">Restricts the search to one source; null searches all active sources.</param>
    /// <param name="mode">Hybrid (default), Semantic (pre-hybrid behavior) or Lexical.</param>
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int topK, Guid? sourceId = null,
        SearchMode mode = SearchMode.Hybrid, CancellationToken ct = default);
}
