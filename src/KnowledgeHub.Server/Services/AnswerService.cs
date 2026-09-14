using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeHub.Server.Chat;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Services;

/// <summary>LLM answer synthesis with [n] citations grounded in retrieved context.</summary>
public sealed partial class AnswerService(
    IChatClient? chatClient,
    ChatProviderOptions options,
    ILogger<AnswerService> logger) : IAnswerService
{
    private const string NoMatchAnswer =
        "The knowledge base has no matching content for this question.";

    private const string SystemPrompt =
        "You are KnowledgeHub's answer engine. Answer ONLY using the numbered context " +
        "passages provided. Cite the passages you use with [n] markers. If the context " +
        "does not contain the answer, say so explicitly — do not invent facts.";

    public bool IsConfigured => chatClient is not null;

    public async Task<AskResponse> AnswerAsync(
        string question, IReadOnlyList<SearchResultItem> context, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        if (context.Count == 0)
            return Result(NoMatchAnswer, [], null, sw);

        var client = chatClient
            ?? throw new ChatProviderException("no chat provider configured (Chat:Provider=none)");

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, BuildUserPrompt(question, context))
            ],
            new ChatOptions
            {
                Temperature = options.Temperature is null ? null : (float)options.Temperature,
                MaxOutputTokens = options.MaxTokens
            },
            cancellationToken);

        var answer = response.Text?.Trim() ?? "";
        if (answer.Length == 0)
            throw new ChatProviderException("chat provider returned an empty answer");

        var citations = ExtractCitations(answer, context);
        logger.LogDebug("Answer synthesized in {LatencyMs} ms (model {Model}, {Citations} citations)",
            sw.Elapsed.TotalMilliseconds, response.ModelId ?? options.Model, citations.Count);
        return Result(answer, citations, response.ModelId ?? options.Model, sw);
    }

    public static string BuildUserPrompt(string question, IReadOnlyList<SearchResultItem> context)
    {
        var sb = new StringBuilder();
        sb.Append("Context:\n\n");
        for (var i = 0; i < context.Count; i++)
        {
            var r = context[i];
            sb.Append('[').Append(i + 1).Append("] ")
              .Append(r.DocumentTitle).Append(" — ").Append(r.SourceName)
              .Append(" (").Append(r.UriReference).Append(")\n")
              .Append(r.ChunkText).Append("\n\n");
        }
        sb.Append("Question: ").Append(question);
        return sb.ToString();
    }

    /// <summary>
    /// Maps [n] markers in the answer to citations. Out-of-range indices are dropped
    /// (SPEC risk mitigation). When the model cited nothing, all context passages are
    /// attached — they grounded the answer attempt.
    /// </summary>
    internal static IReadOnlyList<CitationDto> ExtractCitations(
        string answer, IReadOnlyList<SearchResultItem> context)
    {
        var indices = CitationMarkerRegex().Matches(answer)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n >= 1 && n <= context.Count)
            .Distinct()
            .ToList();

        if (indices.Count == 0)
            indices = Enumerable.Range(1, context.Count).ToList();

        return indices.Select(n =>
        {
            var r = context[n - 1];
            return new CitationDto
            {
                Index = n,
                Source = r.SourceName,
                Title = r.DocumentTitle,
                Uri = r.UriReference,
                Score = r.Score
            };
        }).ToList();
    }

    private static AskResponse Result(
        string answer, IReadOnlyList<CitationDto> citations, string? model, Stopwatch sw) =>
        new()
        {
            Answer = answer,
            Citations = citations,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            Model = model,
            Generated = true
        };

    [GeneratedRegex(@"\[(\d{1,3})\]")]
    private static partial Regex CitationMarkerRegex();
}
