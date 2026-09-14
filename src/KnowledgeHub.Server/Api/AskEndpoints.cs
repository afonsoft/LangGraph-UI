using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>POST /api/ask — RAG with server-side answer synthesis (SPEC-20260914-llm-answer-synthesis RF-003).</summary>
public static class AskEndpoints
{
    public static RouteGroupBuilder MapAskApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ask");

        group.MapPost("/", async (
            AskRequest request,
            ISearchService search,
            IAnswerService answers,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return Results.BadRequest(new { error = "question is required" });

            var mode = SearchEndpoints.ParseMode(request.Mode);
            if (mode is null)
                return Results.BadRequest(new { error = "mode must be hybrid | semantic | lexical" });

            var generate = request.Generate ?? answers.IsConfigured;
            var k = request.TopK is null or <= 0 ? SearchEndpoints.DefaultTopK : Math.Min(request.TopK.Value, SearchEndpoints.MaxTopK);
            var context = await search.SearchAsync(request.Question, k, request.SourceId, mode.Value, ct);

            if (!generate)
            {
                return Results.Ok(new AskResponse
                {
                    Answer = null,
                    Citations = [],
                    LatencyMs = 0,
                    Model = null,
                    Generated = false,
                    Context = context
                });
            }

            if (!answers.IsConfigured)
                return Results.BadRequest(new { error = "chat provider not configured (Chat:Provider=none)" });

            return Results.Ok(await answers.AnswerAsync(request.Question, context, ct));
        });

        return group;
    }
}
