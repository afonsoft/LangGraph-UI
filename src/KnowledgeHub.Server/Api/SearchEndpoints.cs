using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>Unified semantic search endpoint (SPEC-02 RF-004).</summary>
public static class SearchEndpoints
{
    public const int DefaultTopK = 5;
    public const int MaxTopK = 50;

    public static RouteGroupBuilder MapSearchApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search");

        group.MapGet("/", async (ISearchService svc, string? query, int? topK, Guid? sourceId, string? mode, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(new { error = "query is required" });

            // Default "semantic" preserves pre-hybrid API behavior; tools default to hybrid.
            var searchMode = ParseMode(mode);
            if (searchMode is null)
                return Results.BadRequest(new { error = "mode must be hybrid | semantic | lexical" });

            var k = topK is null or <= 0 ? DefaultTopK : Math.Min(topK.Value, MaxTopK);
            var results = await svc.SearchAsync(query, k, sourceId, searchMode.Value, ct);
            return Results.Ok(new SearchResponse { Results = results });
        });

        return group;
    }

    internal static SearchMode? ParseMode(string? mode) => mode switch
    {
        null or "" => SearchMode.Semantic,
        _ when Enum.TryParse<SearchMode>(mode, ignoreCase: true, out var parsed) => parsed,
        _ => null
    };
}
