using System.Text.Json.Nodes;
using KnowledgeHub.Server.Settings;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>
/// Firecrawl proxy tools (SPEC-20260916-firecrawl-mcp-proxy RF-003) — hybrid
/// registration: a static core set is always listed while Enabled (so calls
/// without a key produce a friendly isError), and when an effective API key
/// exists the upstream <c>tools/list</c> is merged in verbatim — names and
/// inputSchema byte-identical to Firecrawl, no aliases. Dynamic entries are
/// authoritative on name collisions.
/// </summary>
public sealed class FirecrawlToolsProvider(
    FirecrawlUpstreamClient upstream,
    IOptions<FirecrawlOptions> options,
    ILogger<FirecrawlToolsProvider> logger) : IToolProvider
{
    internal const string NoKeyMessage =
        "Firecrawl API key not configured — open Settings (/settings) or set Firecrawl__ApiKey.";

    /// <summary>Upstream tools that spend credits or mutate remote state — they
    /// keep the Playground write-confirm gate when the upstream omits hints.</summary>
    private static readonly string[] MutatingPrefixes =
    [
        "firecrawl_crawl", "firecrawl_interact", "firecrawl_agent",
        "firecrawl_monitor_", "firecrawl_feedback", "firecrawl_search_feedback"
    ];

    private readonly FirecrawlOptions _options = options.Value;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private IReadOnlyList<CatalogTool>? _dynamicTools;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    /// <summary>Test seam — supplies upstream protocol tools without a live
    /// <see cref="McpClient"/> session.</summary>
    internal Func<CancellationToken, Task<IReadOnlyList<Tool>>>? DynamicToolsSource { get; set; }

    private static readonly JsonObject ScrapeSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "url":{"type":"string","description":"URL to scrape",
                 "examples":["https://example.com"]},
          "formats":{"type":"array","items":{"type":"string"},
                     "description":"Output formats (e.g. markdown, html, json)"},
          "onlyMainContent":{"type":"boolean","description":"Exclude nav/footer boilerplate"}
        },"required":["url"],
        "examples":[{"url":"https://example.com"},
                    {"url":"https://docs.firecrawl.dev","formats":["markdown"],"onlyMainContent":true}]}
        """)!.AsObject();

    private static readonly JsonObject SearchSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "query":{"type":"string","description":"Search query",
                   "examples":["latest .NET 10 release notes"]},
          "limit":{"type":"integer","description":"Max results"}
        },"required":["query"],
        "examples":[{"query":"latest .NET 10 release notes"}]}
        """)!.AsObject();

    private static readonly JsonObject MapSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "url":{"type":"string","description":"Site URL to map",
                 "examples":["https://docs.firecrawl.dev"]}
        },"required":["url"],
        "examples":[{"url":"https://docs.firecrawl.dev"}]}
        """)!.AsObject();

    private static readonly JsonObject CrawlSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "url":{"type":"string","description":"Starting URL",
                 "examples":["https://docs.firecrawl.dev"]},
          "limit":{"type":"integer","description":"Max pages to crawl"}
        },"required":["url"],
        "examples":[{"url":"https://docs.firecrawl.dev","limit":5}]}
        """)!.AsObject();

    private static readonly JsonObject CrawlStatusSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "id":{"type":"string","description":"Crawl job id",
                "examples":["550e8400-e29b-41d4-a716-446655440000"]}
        },"required":["id"],
        "examples":[{"id":"550e8400-e29b-41d4-a716-446655440000"}]}
        """)!.AsObject();

    private static readonly JsonObject ParseSchema = JsonNode.Parse("""
        {"type":"object","additionalProperties":true,"properties":{
          "url":{"type":"string","description":"Public URL of the document to parse",
                 "examples":["https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf"]},
          "filePath":{"type":"string","description":"Local file path (two-phase upload flow)"}
        },
        "examples":[{"url":"https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf"}]}
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
                logger.LogWarning(ex, "firecrawl tools/list failed — serving cached/static set");
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
            Description = proto.Description ?? $"Firecrawl tool {name} (proxied).",
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
            Name = "firecrawl_scrape",
            Description = "Scrape a single URL — content or structured fields (proxied to Firecrawl).",
            InputSchema = ScrapeSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("firecrawl_scrape", ctx, ct)
        },
        new CatalogTool
        {
            Name = "firecrawl_search",
            Description = "Search the web (proxied to Firecrawl).",
            InputSchema = SearchSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("firecrawl_search", ctx, ct)
        },
        new CatalogTool
        {
            Name = "firecrawl_map",
            Description = "Discover site URLs before extraction (proxied to Firecrawl).",
            InputSchema = MapSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("firecrawl_map", ctx, ct)
        },
        new CatalogTool
        {
            Name = "firecrawl_crawl",
            Description = "Crawl a site/section; polls the job to a terminal state (proxied to Firecrawl — billable).",
            InputSchema = CrawlSchema,
            ReadOnly = false,
            Handler = (ctx, ct) => DispatchAsync("firecrawl_crawl", ctx, ct)
        },
        new CatalogTool
        {
            Name = "firecrawl_check_crawl_status",
            Description = "Check/resume a running crawl job (proxied to Firecrawl).",
            InputSchema = CrawlStatusSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("firecrawl_check_crawl_status", ctx, ct)
        },
        new CatalogTool
        {
            Name = "firecrawl_parse",
            Description = "Parse a PDF/document/spreadsheet/HTML file (proxied to Firecrawl).",
            InputSchema = ParseSchema,
            ReadOnly = true,
            Handler = (ctx, ct) => DispatchAsync("firecrawl_parse", ctx, ct)
        }
    ];
}
