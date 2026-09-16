using System.Text.Json.Nodes;
using KnowledgeHub.Server.Settings;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>
/// Tavily proxy tools (SPEC-20260916-tavily-mcp-proxy RF-003) — hybrid
/// registration: a static core set is always listed while Enabled (so calls
/// without a key produce a friendly isError), and when an effective API key
/// exists the upstream <c>tools/list</c> is merged in verbatim — names and
/// inputSchema byte-identical to Tavily, no aliases. Dynamic entries are
/// authoritative on name collisions.
/// </summary>
public sealed class TavilyToolsProvider(
    TavilyUpstreamClient upstream,
    IOptions<TavilyOptions> options,
    ILogger<TavilyToolsProvider> logger) : IToolProvider
{
    internal const string NoKeyMessage =
        "Tavily API key not configured — open Settings (/settings) or set Tavily__ApiKey.";

    /// <summary>Upstream tools that spend credits or run long jobs — they keep
    /// the Playground write-confirm gate when the upstream omits hints.</summary>
    private static readonly string[] MutatingPrefixes =
    [
        "tavily_crawl", "tavily_research"
    ];

    private readonly TavilyOptions _options = options.Value;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private IReadOnlyList<CatalogTool>? _dynamicTools;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    /// <summary>Test seam — supplies upstream protocol tools without a live
    /// <see cref="McpClient"/> session.</summary>
    internal Func<CancellationToken, Task<IReadOnlyList<Tool>>>? DynamicToolsSource { get; set; }

    private static readonly JsonObject SearchSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "query":{"type":"string","description":"Search query",
                   "examples":["latest .NET 10 release notes"]},
          "max_results":{"type":"integer","description":"Max results (default 5)"},
          "search_depth":{"type":"string","description":"basic | advanced"},
          "include_domains":{"type":"array","items":{"type":"string"}},
          "exclude_domains":{"type":"array","items":{"type":"string"}}
        },"required":["query"],
        "examples":[{"query":"latest .NET 10 release notes"},
                    {"query":"site reliability best practices","max_results":3,"search_depth":"basic"}]}
        """)!.AsObject();

    private static readonly JsonObject ExtractSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "urls":{"type":"array","items":{"type":"string"},
                  "description":"One or more URLs to extract content from",
                  "examples":[["https://example.com"]]}
        },"required":["urls"],
        "examples":[{"urls":["https://example.com"]}]}
        """)!.AsObject();

    private static readonly JsonObject MapSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "url":{"type":"string","description":"Site URL to map",
                 "examples":["https://docs.tavily.com"]},
          "max_depth":{"type":"integer","description":"Max link depth"},
          "limit":{"type":"integer","description":"Max URLs returned"}
        },"required":["url"],
        "examples":[{"url":"https://docs.tavily.com","max_depth":1,"limit":20}]}
        """)!.AsObject();

    private static readonly JsonObject CrawlSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "url":{"type":"string","description":"Starting URL",
                 "examples":["https://docs.tavily.com"]},
          "max_depth":{"type":"integer","description":"Max link depth"},
          "limit":{"type":"integer","description":"Max pages to crawl"},
          "instructions":{"type":"string","description":"Natural-language crawl guidance"}
        },"required":["url"],
        "examples":[{"url":"https://docs.tavily.com","max_depth":1,"limit":5}]}
        """)!.AsObject();

    private static readonly JsonObject ResearchSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "input":{"type":"string","description":"Research question or task",
                   "examples":["Compare .NET 10 minimal APIs vs controllers for a small team"]},
          "model":{"type":"string","description":"Research model (e.g. mini | pro)"}
        },"required":["input"],
        "examples":[{"input":"Compare .NET 10 minimal APIs vs controllers for a small team"}]}
        """)!.AsObject();

    public async Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return [];

        var tools = new Dictionary<string, CatalogTool>(StringComparer.Ordinal);
        foreach (var tool in StaticCoreTools())
            tools[tool.Name] = tool;

        // Dynamic merge only makes sense when an upstream session can authenticate.
        if (await upstream.HasApiKeyAsync(cancellationToken))
            foreach (var tool in await GetDynamicToolsAsync(cancellationToken))
                tools[tool.Name] = tool;

        return [.. tools.Values];
    }

    /// <summary>Drops the cached upstream tools/list — called when the
    /// integration secret changes via Settings.</summary>
    public void InvalidateToolsCache()
    {
        _cacheGate.Wait();
        try
        {
            _dynamicTools = null;
            _cacheExpiresAt = DateTimeOffset.MinValue;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private async Task<IReadOnlyList<CatalogTool>> GetDynamicToolsAsync(CancellationToken cancellationToken)
    {
        if (_dynamicTools is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
            return _dynamicTools;

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (_dynamicTools is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
                return _dynamicTools;

            try
            {
                var protocolTools = DynamicToolsSource is { } source
                    ? await source(cancellationToken)
                    : (await upstream.ListToolsAsync(cancellationToken))
                        .Select(t => t.ProtocolTool).ToList();
                _dynamicTools = protocolTools.Select(MapTool).ToList();
                _cacheExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.ToolsCacheSeconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // last-known-good wins; empty when nothing was ever fetched.
                logger.LogWarning(ex, "tavily tools/list failed — serving cached/static set");
            }
            return _dynamicTools ?? [];
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private CatalogTool MapTool(Tool proto)
    {
        var schema = JsonNode.Parse(proto.InputSchema.GetRawText()) as JsonObject
            ?? new JsonObject { ["type"] = "object" };
        var name = proto.Name;
        return new CatalogTool
        {
            Name = name,
            Description = proto.Description ?? $"Tavily tool {name} (proxied).",
            InputSchema = schema,
            ReadOnly = proto.Annotations?.ReadOnlyHint
                ?? !MutatingPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)),
            Handler = (ctx, ct) => DispatchAsync(name, ctx, ct)
        };
    }

    /// <summary>Shared dispatch: friendly isError without a key, transparent
    /// passthrough with one (RF-005 / CA-002).</summary>
    private async ValueTask<CallToolResult> DispatchAsync(
        string toolName, ToolCallContext ctx, CancellationToken ct)
    {
        if (!await upstream.HasApiKeyAsync(ct))
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = NoKeyMessage }]
            };
        return await upstream.CallAsync(toolName, ctx.Arguments, ct);
    }

    private IReadOnlyList<CatalogTool> StaticCoreTools() =>
    [
        new CatalogTool
        {
            Name = "tavily_search",
            Description = "Search the web (proxied to Tavily).",
            InputSchema = SearchSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("tavily_search", ctx, ct)
        },
        new CatalogTool
        {
            Name = "tavily_extract",
            Description = "Extract clean content from one or more URLs (proxied to Tavily).",
            InputSchema = ExtractSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("tavily_extract", ctx, ct)
        },
        new CatalogTool
        {
            Name = "tavily_map",
            Description = "Discover site URLs before extraction (proxied to Tavily).",
            InputSchema = MapSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("tavily_map", ctx, ct)
        },
        new CatalogTool
        {
            Name = "tavily_crawl",
            Description = "Crawl a site/section (proxied to Tavily — billable/long-running).",
            InputSchema = CrawlSchema,
            ReadOnly = false,
            Handler = (ctx, ct) => DispatchAsync("tavily_crawl", ctx, ct)
        },
        new CatalogTool
        {
            Name = "tavily_research",
            Description = "Deep research on a question (proxied to Tavily — billable/long-running).",
            InputSchema = ResearchSchema,
            ReadOnly = false,
            Handler = (ctx, ct) => DispatchAsync("tavily_research", ctx, ct)
        }
    ];
}
