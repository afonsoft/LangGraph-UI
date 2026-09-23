using System.Text;
using System.Text.RegularExpressions;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Search;

/// <summary>One rerank score for a candidate chunk.</summary>
public sealed record RerankScore(Guid ChunkId, double Score);

/// <summary>
/// Post-fusion rerank stage (SPEC-20260923-retrieval-quality RF-002).
/// Implementations score the hydrated candidate window against the query;
/// an empty result means "keep the fused order" (fail-open).
/// </summary>
public interface IReranker
{
    Task<IReadOnlyList<RerankScore>> RerankAsync(
        string query, IReadOnlyList<SearchResultItem> candidates, CancellationToken ct = default);
}

/// <summary>Default: no reranking — fused RRF order is preserved.</summary>
public sealed class NoOpReranker : IReranker
{
    public static readonly NoOpReranker Instance = new();
    public Task<IReadOnlyList<RerankScore>> RerankAsync(
        string query, IReadOnlyList<SearchResultItem> candidates, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RerankScore>>([]);
}

/// <summary>
/// Scores each candidate 0–10 against the query in a single batched prompt.
/// Candidates are truncated to <see cref="MaxChars"/> each. Parse failures for
/// individual lines are skipped; a wholesale failure returns empty (fused
/// order preserved upstream).
/// </summary>
public sealed partial class LlmReranker(
    IChatClient chatClient,
    ILogger<LlmReranker> logger) : IReranker
{
    private const int MaxChars = 1200;

    public async Task<IReadOnlyList<RerankScore>> RerankAsync(
        string query, IReadOnlyList<SearchResultItem> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return [];

        var sb = new StringBuilder();
        sb.Append("Score each passage 0-10 for how relevant it is to the query. ")
          .Append("Reply with one line per passage: '<n>: <score>'. No other text.\n\n")
          .Append("Query: ").Append(query).Append("\n\n");
        for (var i = 0; i < candidates.Count; i++)
        {
            var text = candidates[i].ChunkText;
            if (text.Length > MaxChars)
                text = text[..MaxChars];
            sb.Append('[').Append(i + 1).Append("] ").Append(text).Append("\n\n");
        }

        var llmSw = System.Diagnostics.Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, sb.ToString())],
                new ChatOptions { Temperature = 0, MaxOutputTokens = candidates.Count * 8 + 16 },
                ct);
        }
        finally
        {
            Telemetry.KnowledgeHubMetrics.LlmDuration.Record(llmSw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", chatClient.GetType().Name),
                new KeyValuePair<string, object?>("model", null),
                new KeyValuePair<string, object?>("kind", "rerank"));
        }

        var scores = new List<RerankScore>(candidates.Count);
        foreach (Match m in ScoreLineRegex().Matches(response.Text ?? ""))
        {
            var index = int.Parse(m.Groups[1].Value) - 1;
            if (index < 0 || index >= candidates.Count)
                continue;
            if (!double.TryParse(m.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture, out var score))
                continue;
            if (candidates[index].ChunkId is { } chunkId)
                scores.Add(new RerankScore(chunkId, Math.Clamp(score, 0, 10)));
        }

        logger.LogInformation("Rerank scored {Scored}/{Total} candidates", scores.Count, candidates.Count);
        return scores;
    }

    [GeneratedRegex(@"(?m)^\s*(\d{1,2})\s*[:.\)]\s*(\d+(?:\.\d+)?)")]
    private static partial Regex ScoreLineRegex();
}
