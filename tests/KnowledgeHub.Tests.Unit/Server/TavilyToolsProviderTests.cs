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

// Covers SPEC-20260916-tavily-mcp-proxy RF-003/RF-004/RF-005: static core
// tools with examples, friendly no-key error, dynamic merge semantics.
public class TavilyToolsProviderTests
{
    private static readonly IServiceProvider EmptyServices =
        new ServiceCollection().BuildServiceProvider();

    private static (TavilyToolsProvider provider, TavilyUpstreamClient client) Build(
        bool enabled = true, string? storedKey = null, string? envKey = null,
        int toolsCacheSeconds = 300)
    {
        var options = new TavilyOptions
        {
            Enabled = enabled,
            ApiKey = envKey ?? "",
            ToolsCacheSeconds = toolsCacheSeconds
        };
        var client = new TavilyUpstreamClient(
            Options.Create(options),
            new FakeSecretStore(tavilyKey: storedKey),
            NullLoggerFactory.Instance,
            NullLogger<TavilyUpstreamClient>.Instance);
        var provider = new TavilyToolsProvider(
            client, Options.Create(options), NullLogger<TavilyToolsProvider>.Instance);
        return (provider, client);
    }

    private static Tool UpstreamTool(string name, string? description = null, bool? readOnly = null,
        string schemaJson = """{"type":"object"}""") =>
        new()
        {
            Name = name,
            Description = description,
            InputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone(),
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
            "tavily_search", "tavily_extract", "tavily_map",
            "tavily_crawl", "tavily_research"
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

        Assert.False(tools.Single(t => t.Name == "tavily_crawl").ReadOnly);
        Assert.False(tools.Single(t => t.Name == "tavily_research").ReadOnly);
        Assert.True(tools.Single(t => t.Name == "tavily_search").ReadOnly);
    }

    [Fact]
    public async Task CallTool_WithoutKey_ReturnsFriendlyIsError()
    {
        var (provider, _) = Build();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var search = tools.Single(t => t.Name == "tavily_search");

        var ctx = new ToolCallContext
        {
            Services = EmptyServices,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["query"] = JsonDocument.Parse("\"hello\"").RootElement
            }
        };
        var result = await search.Handler(ctx, CancellationToken.None);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("Tavily API key", text);
        Assert.DoesNotContain("tvly-", text); // never echoes key material
    }

    [Fact]
    public async Task WithKey_MergesDynamicTools_VerbatimNamesAndSchemas()
    {
        var (provider, _) = Build(storedKey: "tvly-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("tavily_new_thing", "upstream-only tool"),
            UpstreamTool("tavily_search", "upstream-authoritative search")
        ]);

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var byName = tools.ToDictionary(t => t.Name);

        Assert.Contains("tavily_new_thing", byName.Keys);
        Assert.Equal("upstream-authoritative search", byName["tavily_search"].Description);
        Assert.Equal(6, tools.Count); // 5 static + 1 new dynamic (search overridden)
    }

    [Fact]
    public async Task DynamicTool_KnownName_GetsCuratedExamples()
    {
        // Dynamic entries override the static core on collisions — the upstream
        // schema would lose the static examples without the curated injection.
        var (provider, _) = Build(storedKey: "tvly-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("tavily_search")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "tavily_search");

        var examples = Assert.IsType<System.Text.Json.Nodes.JsonArray>(tool.InputSchema["examples"]);
        Assert.Equal("latest .NET 10 release notes",
            examples[0]!["query"]!.GetValue<string>());
    }

    [Fact]
    public async Task DynamicTool_UpstreamExamples_Preserved()
    {
        var (provider, _) = Build(storedKey: "tvly-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("tavily_search",
                schemaJson: """{"type":"object","examples":[{"query":"upstream wins"}]}""")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "tavily_search");

        var examples = Assert.IsType<System.Text.Json.Nodes.JsonArray>(tool.InputSchema["examples"]);
        Assert.Single(examples);
        Assert.Equal("upstream wins", examples[0]!["query"]!.GetValue<string>());
    }

    [Fact]
    public async Task DynamicTool_UnknownName_NoExamplesInjected()
    {
        var (provider, _) = Build(storedKey: "tvly-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("tavily_thing")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "tavily_thing");

        Assert.False(tool.InputSchema.ContainsKey("examples"));
    }

    [Fact]
    public async Task DynamicTool_MutatingPrefixWithoutHint_NotReadOnly()
    {
        var (provider, _) = Build(storedKey: "tvly-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("tavily_research"),
            UpstreamTool("tavily_thing"),               // unknown → defaults read-only
            UpstreamTool("tavily_crawl", readOnly: true) // upstream hint wins
        ]);

        var tools = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .ToDictionary(t => t.Name);

        Assert.False(tools["tavily_research"].ReadOnly);
        Assert.True(tools["tavily_thing"].ReadOnly);
        Assert.True(tools["tavily_crawl"].ReadOnly);
    }

    [Fact]
    public async Task DynamicListFailure_FallsBackToStaticCore()
    {
        var (provider, _) = Build(storedKey: "tvly-test");
        provider.DynamicToolsSource = _ => throw new HttpRequestException("upstream down");

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.Equal(5, tools.Count);
        Assert.Contains(tools, t => t.Name == "tavily_search");
    }

    [Fact]
    public async Task DynamicListFailure_KeepsLastKnownGood()
    {
        var (provider, _) = Build(storedKey: "tvly-test", toolsCacheSeconds: 1);
        var calls = 0;
        provider.DynamicToolsSource = _ =>
        {
            calls++;
            if (calls == 1)
                return Task.FromResult<IReadOnlyList<Tool>>([UpstreamTool("tavily_new_thing")]);
            throw new HttpRequestException("flaky");
        };

        await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        await Task.Delay(1100); // let the cache expire so the second call refetches
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.Contains(tools, t => t.Name == "tavily_new_thing"); // cached set survives
    }

    private sealed class FakeSecretStore(string? tavilyKey = null)
        : IIntegrationSecretStore
    {
        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(provider == IntegrationProviders.Tavily ? tavilyKey : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
