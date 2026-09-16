using System.Text.Json;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Server.Mcp.Upstream;
using KnowledgeHub.Server.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260916-firecrawl-mcp-proxy RF-003/RF-005/RF-009: static core
// tools with examples, friendly no-key error, dynamic merge semantics.
public class FirecrawlToolsProviderTests
{
    private static readonly IServiceProvider EmptyServices =
        new ServiceCollection().BuildServiceProvider();

    private static (FirecrawlToolsProvider provider, FirecrawlUpstreamClient client) Build(
        bool enabled = true, string? storedKey = null, string? envKey = null,
        int toolsCacheSeconds = 300)
    {
        var options = new FirecrawlOptions
        {
            Enabled = enabled,
            ApiKey = envKey ?? "",
            ToolsCacheSeconds = toolsCacheSeconds
        };
        var client = new FirecrawlUpstreamClient(
            Options.Create(options),
            new FakeSecretStore(firecrawlKey: storedKey),
            NullLoggerFactory.Instance,
            NullLogger<FirecrawlUpstreamClient>.Instance);
        var provider = new FirecrawlToolsProvider(
            client, Options.Create(options), NullLogger<FirecrawlToolsProvider>.Instance);
        return (provider, client);
    }

    private static Tool UpstreamTool(string name, string? description = null, bool? readOnly = null) =>
        new()
        {
            Name = name,
            Description = description,
            InputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
            Annotations = readOnly is null ? null : new ToolAnnotations { ReadOnlyHint = readOnly }
        };

    [Fact]
    public async Task Disabled_ReturnsEmpty()
    {
        var (provider, _) = Build(enabled: false);
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.Empty(tools);
    }

    [Fact]
    public async Task Enabled_NoKey_ListsStaticCoreWithExactUpstreamNames()
    {
        var (provider, _) = Build();
        var names = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Select(t => t.Name).ToHashSet();

        Assert.True(names.SetEquals(
        [
            "firecrawl_scrape", "firecrawl_search", "firecrawl_map",
            "firecrawl_crawl", "firecrawl_check_crawl_status", "firecrawl_parse"
        ]));
    }

    [Fact]
    public async Task StaticCoreTools_HaveRootExamples()
    {
        var (provider, _) = Build();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        foreach (var tool in tools)
            Assert.True(
                tool.InputSchema["examples"] is System.Text.Json.Nodes.JsonArray { Count: > 0 },
                $"{tool.Name} must carry schema examples for the Playground");
    }

    [Fact]
    public async Task StaticCoreTools_ReadOnlyFlags()
    {
        var (provider, _) = Build();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.False(tools.Single(t => t.Name == "firecrawl_crawl").ReadOnly);
        Assert.True(tools.Single(t => t.Name == "firecrawl_scrape").ReadOnly);
    }

    [Fact]
    public async Task CallTool_WithoutKey_ReturnsFriendlyIsError()
    {
        var (provider, _) = Build();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var scrape = tools.Single(t => t.Name == "firecrawl_scrape");

        var ctx = new ToolCallContext
        {
            Services = EmptyServices,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["url"] = JsonDocument.Parse("\"https://example.com\"").RootElement
            }
        };
        var result = await scrape.Handler(ctx, CancellationToken.None);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("Firecrawl API key", text);
        Assert.DoesNotContain("fc-", text); // never echoes key material
    }

    [Fact]
    public async Task WithKey_MergesDynamicTools_VerbatimNamesAndSchemas()
    {
        var (provider, _) = Build(storedKey: "fc-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("firecrawl_agent", "upstream agent tool"),
            UpstreamTool("firecrawl_scrape", "upstream-authoritative scrape")
        ]);

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var byName = tools.ToDictionary(t => t.Name);

        Assert.Contains("firecrawl_agent", byName.Keys);
        Assert.Equal("upstream-authoritative scrape", byName["firecrawl_scrape"].Description);
        Assert.Equal(7, tools.Count); // 6 static + 1 new dynamic (scrape overridden)
    }

    [Fact]
    public async Task DynamicTool_MutatingPrefixWithoutHint_NotReadOnly()
    {
        var (provider, _) = Build(storedKey: "fc-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("firecrawl_monitor_delete"),
            UpstreamTool("firecrawl_thing"),           // unknown → defaults read-only
            UpstreamTool("firecrawl_crawl", readOnly: true) // upstream hint wins
        ]);

        var tools = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .ToDictionary(t => t.Name);

        Assert.False(tools["firecrawl_monitor_delete"].ReadOnly);
        Assert.True(tools["firecrawl_thing"].ReadOnly);
        Assert.True(tools["firecrawl_crawl"].ReadOnly);
    }

    [Fact]
    public async Task DynamicListFailure_FallsBackToStaticCore()
    {
        var (provider, _) = Build(storedKey: "fc-test");
        provider.DynamicToolsSource = _ => throw new HttpRequestException("upstream down");

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.Equal(6, tools.Count);
        Assert.Contains(tools, t => t.Name == "firecrawl_scrape");
    }

    [Fact]
    public async Task DynamicListFailure_KeepsLastKnownGood()
    {
        var (provider, _) = Build(storedKey: "fc-test", toolsCacheSeconds: 1);
        var calls = 0;
        provider.DynamicToolsSource = _ =>
        {
            calls++;
            if (calls == 1)
                return Task.FromResult<IReadOnlyList<Tool>>([UpstreamTool("firecrawl_agent")]);
            throw new HttpRequestException("flaky");
        };

        await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        await Task.Delay(1100); // let the cache expire so the second call refetches
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.Contains(tools, t => t.Name == "firecrawl_agent"); // cached set survives
    }

    private sealed class FakeSecretStore(string? firecrawlKey = null, string? deepwikiKey = null)
        : IIntegrationSecretStore
    {
        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(provider == IntegrationProviders.Firecrawl ? firecrawlKey
                : provider == IntegrationProviders.DeepWiki ? deepwikiKey
                : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
