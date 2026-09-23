using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.Server.RateLimiting;

/// <summary>
/// SPEC-20260923-rate-limiting RF-003: in-dispatch limiter for MCP
/// <c>tools/call</c>. JSON-RPC has no 429 — over-limit calls get a friendly
/// <c>isError</c> result instead of an exception. LLM-spending and
/// write/sync tools are charged; read-only utility tools pass through.
/// </summary>
public sealed class McpToolRateLimiter : IDisposable
{
    private static readonly HashSet<string> LlmTools = new(StringComparer.Ordinal)
        { "ask_knowledge", "agent_chat", "search_knowledge" };

    private static readonly HashSet<string> SyncTools = new(StringComparer.Ordinal)
        { "write_knowledge", "write_note" };

    private readonly RateLimitOptions _options;
    private readonly ILogger<McpToolRateLimiter> _logger;
    private readonly PartitionedRateLimiter<string> _llm;
    private readonly PartitionedRateLimiter<string> _sync;

    public McpToolRateLimiter(RateLimitOptions options, ILogger<McpToolRateLimiter> logger)
    {
        _options = options;
        _logger = logger;
        _llm = Build(options.LlmPermitLimit, options.AnonymousLlmPermitLimit, options.LlmWindowSeconds, sliding: true);
        _sync = Build(options.SyncPermitLimit, options.SyncPermitLimit, options.SyncWindowSeconds, sliding: false);
    }

    /// <summary>
    /// Attempts to charge one call. Returns true when the call may proceed;
    /// on rejection <paramref name="retryAfterSeconds"/> carries the hint.
    /// </summary>
    public bool TryAcquire(string toolName, HttpContext? http, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!_options.Enabled)
            return true;

        var limiter = LlmTools.Contains(toolName) ? _llm
            : SyncTools.Contains(toolName) ? _sync
            : null;
        if (limiter is null)
            return true;

        var (key, kind, _) = CallerPartitioner.Resolve(http, _options.TrustForwardedHeaders);
        using var lease = limiter.AttemptAcquire(key);
        if (lease.IsAcquired)
            return true;

        retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : _options.LlmWindowSeconds;

        _logger.LogWarning(
            "MCP tool {ToolName} rate limited for {Kind} partition (retry in {RetryAfter}s)",
            toolName, kind, retryAfterSeconds);
        return false;
    }

    private static PartitionedRateLimiter<string> Build(int permit, int anonPermit, int windowSeconds, bool sliding) =>
        PartitionedRateLimiter.Create<string, string>(key =>
        {
            var limit = key.StartsWith(CallerPartitioner.AnonymousPrefix, StringComparison.Ordinal)
                ? anonPermit
                : permit;
            return sliding
                ? RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                })
                : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0
                });
        });

    public void Dispose()
    {
        _llm.Dispose();
        _sync.Dispose();
    }
}
