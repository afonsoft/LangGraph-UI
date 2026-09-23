namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Optional sink for per-request MCP metrics, resolved lazily inside request
/// filters so the engine project never depends on the server's Meter.
/// Registered by the server; absent → filters skip metric recording.
/// </summary>
public interface IMcpRequestMetrics
{
    /// <summary>Records one JSON-RPC request. <paramref name="method"/> is the
    /// JSON-RPC method name (e.g. <c>tools/list</c>); <paramref name="sessionMode"/>
    /// is <c>stateful</c> or <c>stateless</c>.</summary>
    void Record(string method, string sessionMode, bool succeeded);
}

/// <summary>No-op fallback registered by the engine when no metrics sink exists.</summary>
internal sealed class NullMcpRequestMetrics : IMcpRequestMetrics
{
    public static readonly NullMcpRequestMetrics Instance = new();

    public void Record(string method, string sessionMode, bool succeeded) { }
}
