using KnowledgeHub.Server.Search;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// SPEC-20260924-corrective-rag: wraps <see cref="ISearchService"/> with a
/// retrieve → grade → retry/abstain loop (CRAG-lite). Weak evidence triggers up
/// to <c>Search:Grading:MaxRetries</c> re-searches with a rewritten query; the
/// best-graded attempt wins. Insufficient evidence short-circuits generation —
/// callers get <see cref="RetrievalOutcome"/> and decide abstention, so no LLM
/// synthesis call is wasted on context that cannot support an answer.
/// </summary>
public sealed class CorrectiveRetrievalService(
    ISearchService search,
    IRetrievalGrader grader,
    IQueryRewriter rewriter,
    IConfiguration configuration,
    ILogger<CorrectiveRetrievalService> logger)
{
    public sealed record RetrievalOutcome(
        IReadOnlyList<SearchResultItem> Results,
        RetrievalGrading Grading,
        bool Retried,
        string EffectiveQuery);

    public bool GradingEnabled =>
        !string.Equals(
            configuration.GetValue("Search:Grading:Mode", "off"), "off",
            StringComparison.OrdinalIgnoreCase);

    public async Task<RetrievalOutcome> RetrieveAsync(
        string query, int topK, Guid? sourceId,
        SearchMode mode, ResolvedSearchFilter? filter,
        string? conversationContext = null, CancellationToken ct = default)
    {
        var results = await search.SearchAsync(query, topK, sourceId, mode, filter, conversationContext, ct);
        if (!GradingEnabled)
            return new RetrievalOutcome(results, new RetrievalGrading(RetrievalGrade.Sufficient, 0), false, query);

        var grading = await grader.GradeAsync(query, results, ct);
        var retries = 0;
        var effectiveQuery = query;
        var maxRetries = Math.Clamp(configuration.GetValue("Search:Grading:MaxRetries", 1), 0, 2);

        while (grading.Grade == RetrievalGrade.Weak && retries < maxRetries)
        {
            var rewritten = await rewriter.RewriteAsync(query, ct);
            if (string.IsNullOrWhiteSpace(rewritten)
                || string.Equals(rewritten, effectiveQuery, StringComparison.Ordinal))
                break;

            retries++;
            effectiveQuery = rewritten;
            logger.LogInformation("Weak retrieval graded — retrying with rewritten query");
            var retryResults = await search.SearchAsync(rewritten, topK, sourceId, mode, filter, conversationContext, ct);
            var retryGrading = await grader.GradeAsync(rewritten, retryResults, ct);

            // Keep the better attempt; the grade reflects what we will answer from.
            if (IsBetter(retryGrading, retryResults, grading, results))
                results = retryResults;
            grading = retryGrading;
        }

        ActivityTag(grading, retries > 0);
        return new RetrievalOutcome(results, grading, retries > 0, effectiveQuery);
    }

    /// <summary>Honest-abstention response — never invokes the synthesis LLM.</summary>
    public AskResponse BuildAbstention(string question, RetrievalOutcome outcome)
    {
        var citations = outcome.Results.Take(3).Select((r, i) => new CitationDto
        {
            Index = i + 1,
            Source = r.SourceName,
            Title = r.DocumentTitle,
            Uri = r.UriReference,
            Path = r.SourceType == SourceType.ObsidianVault ? r.UriReference : null,
            Score = r.Score,
            SuspicionFlags = r.SuspicionFlags,
            Components = r.Components
        }).ToList();

        return new AskResponse
        {
            Answer = "I could not find sufficient evidence in the knowledge base to answer this question." +
                     (citations.Count > 0 ? " The closest passages are listed below." : ""),
            Citations = citations,
            LatencyMs = 0,
            Model = null,
            Generated = false,
            InsufficientEvidence = true,
            RetrievalGrade = outcome.Grading.Grade.ToString().ToLowerInvariant(),
            Retried = outcome.Retried
        };
    }

    private static bool IsBetter(
        RetrievalGrading a, IReadOnlyList<SearchResultItem> ra,
        RetrievalGrading b, IReadOnlyList<SearchResultItem> rb)
    {
        if (a.Grade != b.Grade)
            return a.Grade > b.Grade;
        if (ra.Count != rb.Count)
            return ra.Count > rb.Count;
        var sa = ra.Count > 0 ? ra[0].ScoreBreakdown?.Fused ?? ra[0].Score : 0;
        var sb = rb.Count > 0 ? rb[0].ScoreBreakdown?.Fused ?? rb[0].Score : 0;
        return sa > sb;
    }

    private void ActivityTag(RetrievalGrading grading, bool retried)
    {
        var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("search.grade", grading.Grade.ToString().ToLowerInvariant());
        if (retried)
            activity?.SetTag("search.retried", true);
    }
}
