using System.Diagnostics.Metrics;
using KnowledgeHub.McpEngine.Activity;

namespace KnowledgeHub.Server.Telemetry;

/// <summary>
/// Central <see cref="Meter"/> for the server (SPEC-20260923-observability-metrics RF-001).
/// Instruments are static singletons — allocation-light, safe on hot paths, and
/// no-ops when no listener is attached. All tag values come from
/// <see cref="TelemetryTags"/>-vetted sets: no query text, titles, PII or secrets.
/// </summary>
public sealed class KnowledgeHubMetrics : IMcpRequestMetrics
{
    /// <summary>Meter name — also the <c>ActivitySource</c> name for correlated traces.</summary>
    public const string MeterName = "KnowledgeHub.Server";

    public static readonly Meter Meter = new(MeterName, "0.1.0");

    /// <summary>End-to-end search latency (ms). Tags: mode, cache_hit.</summary>
    public static readonly Histogram<double> SearchDuration =
        Meter.CreateHistogram<double>("knowledgehub.search.duration", "ms");

    /// <summary>Embedding provider call latency (ms). Tags: provider, model.</summary>
    public static readonly Histogram<double> EmbeddingDuration =
        Meter.CreateHistogram<double>("knowledgehub.embedding.duration", "ms");

    /// <summary>Vector store KNN latency (ms). Tags: store.</summary>
    public static readonly Histogram<double> VectorSearchDuration =
        Meter.CreateHistogram<double>("knowledgehub.vector_search.duration", "ms");

    /// <summary>Lexical (FTS5) search latency (ms). No tags.</summary>
    public static readonly Histogram<double> LexicalDuration =
        Meter.CreateHistogram<double>("knowledgehub.lexical.duration", "ms");

    /// <summary>LLM call latency (ms). Tags: provider, model, kind (ask|agent|rewrite|rerank|summary).</summary>
    public static readonly Histogram<double> LlmDuration =
        Meter.CreateHistogram<double>("knowledgehub.llm.duration", "ms");

    /// <summary>MCP tool execution latency (ms). Tags: tool.</summary>
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("knowledgehub.tool.duration", "ms");

    /// <summary>Source sync duration (ms). Tags: status.</summary>
    public static readonly Histogram<double> SyncDuration =
        Meter.CreateHistogram<double>("knowledgehub.sync.duration", "ms");

    /// <summary>Chunks persisted per sync. Tags: status.</summary>
    public static readonly Counter<long> SyncChunks =
        Meter.CreateCounter<long>("knowledgehub.sync.chunks");

    /// <summary>Distributed cache hits. Tags: region.</summary>
    public static readonly Counter<long> CacheHits =
        Meter.CreateCounter<long>("knowledgehub.cache.hits");

    /// <summary>Distributed cache misses. Tags: region.</summary>
    public static readonly Counter<long> CacheMisses =
        Meter.CreateCounter<long>("knowledgehub.cache.misses");

    /// <summary>JSON-RPC requests handled by the MCP dispatcher. Tags: method, session_mode.</summary>
    public static readonly Counter<long> McpRequests =
        Meter.CreateCounter<long>("knowledgehub.mcp.requests");

    /// <inheritdoc />
    public void Record(string method, string sessionMode, bool succeeded)
    {
        McpRequests.Add(1,
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("session_mode", sessionMode),
            new KeyValuePair<string, object?>("succeeded", succeeded));
    }
}
