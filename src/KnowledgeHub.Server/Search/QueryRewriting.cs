using KnowledgeHub.Server.Caching;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace KnowledgeHub.Server.Search;

/// <summary>
/// Optional pre-retrieval stage (SPEC-20260923-retrieval-quality RF-001):
/// rewrites the raw user query into a retrieval-oriented form. Fail-open —
/// implementations must return the original query when disabled or on error.
/// </summary>
public interface IQueryRewriter
{
    Task<string> RewriteAsync(string query, CancellationToken ct = default);
}

/// <summary>
/// LLM-backed rewriter. Enabled via <c>Search:QueryRewrite:Enabled</c>
/// (default false) and requires a configured <see cref="IChatClient"/>.
/// Rewrites are cached in <see cref="IDistributedCache"/> keyed
/// <c>rewrite:{model}:{sha256(query)}</c> for 24h. Any provider failure falls
/// back to the original query.
/// </summary>
public sealed class LlmQueryRewriter(
    IServiceProvider services,
    IDistributedCache cache,
    IConfiguration configuration,
    ILogger<LlmQueryRewriter> logger) : IQueryRewriter
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private const string RewritePrompt =
        "Rewrite the user's text as a concise search query for a knowledge base: " +
        "expand acronyms, normalize terminology, remove conversational filler. " +
        "Return only the rewritten query — no explanation, no quotes.";

    public async Task<string> RewriteAsync(string query, CancellationToken ct = default)
    {
        if (!configuration.GetValue("Search:QueryRewrite:Enabled", false))
            return query;

        var chat = services.GetService<IChatClient>();
        if (chat is null)
            return query;

        var key = $"rewrite:{configuration.GetValue("Chat:Model", "default")}:{CacheKeys.Hash(query)}";
        var cached = await SafeCache.GetStringAsync(cache, key, logger, ct);
        if (cached is not null)
            return cached;

        try
        {
            var response = await chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, RewritePrompt),
                    new ChatMessage(ChatRole.User, query)
                ],
                new ChatOptions { Temperature = 0, MaxOutputTokens = 128 },
                ct);
            var rewritten = response.Text?.Trim();
            if (string.IsNullOrEmpty(rewritten) || rewritten.Length > query.Length * 4)
            {
                logger.LogWarning("Query rewrite returned unusable output ({Length} chars) — using original",
                    rewritten?.Length ?? 0);
                return query;
            }

            logger.LogInformation("Query rewritten ({In} → {Out} chars)", query.Length, rewritten.Length);
            await SafeCache.SetStringAsync(cache, key, rewritten, Ttl, logger, ct);
            return rewritten;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Query rewrite failed — using original query");
            return query;
        }
    }
}
