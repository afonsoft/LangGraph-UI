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

// Covers SPEC-20260922-context7-mcp-proxy RF-003/RF-004/RF-005: static core
// tools with examples, friendly no-key error, dynamic merge semantics.
public class Context7ToolsProviderTests
{
    private static readonly IServiceProvider EmptyServices =
        new ServiceCollection().BuildServiceProvider();

    private static (Context7ToolsProvider provider, Context7UpstreamClient client) Build(
        bool enabled = true, string? storedKey = null, string? envKey = null,
        int toolsCacheSeconds = 300)
    {
        var options = new Context7Options
        {
            Enabled = enabled,
            ApiKey = envKey ?? "",
            ToolsCacheSeconds = toolsCacheSeconds
        };
        var client = new Context7UpstreamClient(
            Options.Create(options),
            new FakeSecretStore(context7Key: storedKey),
            NullLoggerFactory.Instance,
            NullLogger<Context7UpstreamClient>.Instance);
        var provider = new Context7ToolsProvider(
            client, Options.Create(options), NullLogger<Context7ToolsProvider>.Instance);
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

        Assert.True(names.SetEquals(["resolve-library-id", "query-docs"]));
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
    public async Task StaticCoreTools_AllReadOnly()
    {
        var (provider, _) = Build();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.All(tools, t => Assert.True(t.ReadOnly, $"{t.Name} must be read-only"));
    }

    [Fact]
    public async Task CallTool_WithoutKey_ReturnsFriendlyIsError()
    {
        var (provider, _) = Build();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var resolve = tools.Single(t => t.Name == "resolve-library-id");

        var ctx = new ToolCallContext
        {
            Services = EmptyServices,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["query"] = JsonDocument.Parse("\"routing\"").RootElement,
                ["libraryName"] = JsonDocument.Parse("\"Next.js\"").RootElement
            }
        };
        var result = await resolve.Handler(ctx, CancellationToken.None);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("Context7 API key", text);
        Assert.DoesNotContain("ctx7sk-", text); // never echoes key material
    }

    [Fact]
    public async Task WithKey_MergesDynamicTools_VerbatimNamesAndSchemas()
    {
        var (provider, _) = Build(storedKey: "ctx7sk-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("future-tool", "upstream-only tool"),
            UpstreamTool("query-docs", "upstream-authoritative docs")
        ]);

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var byName = tools.ToDictionary(t => t.Name);

        Assert.Contains("future-tool", byName.Keys);
        Assert.Equal("upstream-authoritative docs", byName["query-docs"].Description);
        Assert.Equal(3, tools.Count); // 2 static + 1 new dynamic (query-docs overridden)
    }

    [Fact]
    public async Task DynamicTool_KnownName_GetsCuratedExamples()
    {
        // Dynamic entries override the static core on collisions — the upstream
        // schema would lose the static examples without the curated injection.
        var (provider, _) = Build(storedKey: "ctx7sk-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("resolve-library-id")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "resolve-library-id");

        var examples = Assert.IsType<System.Text.Json.Nodes.JsonArray>(tool.InputSchema["examples"]);
        Assert.Equal("Next.js",
            examples[0]!["libraryName"]!.GetValue<string>());
    }

    [Fact]
    public async Task DynamicTool_UpstreamExamples_Preserved()
    {
        var (provider, _) = Build(storedKey: "ctx7sk-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("resolve-library-id",
                schemaJson: """{"type":"object","examples":[{"libraryName":"upstream wins"}]}""")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "resolve-library-id");

        var examples = Assert.IsType<System.Text.Json.Nodes.JsonArray>(tool.InputSchema["examples"]);
        Assert.Single(examples);
        Assert.Equal("upstream wins", examples[0]!["libraryName"]!.GetValue<string>());
    }

    [Fact]
    public async Task DynamicTool_UnknownName_NoExamplesInjected()
    {
        var (provider, _) = Build(storedKey: "ctx7sk-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("other-tool")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "other-tool");

        Assert.False(tool.InputSchema.ContainsKey("examples"));
    }

    [Fact]
    public async Task DynamicTool_NoHint_DefaultsReadOnly()
    {
        // Context7 exposes no write tools — absent upstream hints default to
        // read-only (no mutating prefixes).
        var (provider, _) = Build(storedKey: "ctx7sk-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("anything"),
            UpstreamTool("marked-write", readOnly: false) // upstream hint wins
        ]);

        var tools = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .ToDictionary(t => t.Name);

        Assert.True(tools["anything"].ReadOnly);
        Assert.False(tools["marked-write"].ReadOnly);
    }

    [Fact]
    public async Task DynamicListFailure_FallsBackToStaticCore()
    {
        var (provider, _) = Build(storedKey: "ctx7sk-test");
        provider.DynamicToolsSource = _ => throw new HttpRequestException("upstream down");

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "resolve-library-id");
    }

    [Fact]
    public async Task DynamicListFailure_KeepsLastKnownGood()
    {
        var (provider, _) = Build(storedKey: "ctx7sk-test", toolsCacheSeconds: 1);
        var calls = 0;
        provider.DynamicToolsSource = _ =>
        {
            calls++;
            if (calls == 1)
                return Task.FromResult<IReadOnlyList<Tool>>([UpstreamTool("future-tool")]);
            throw new HttpRequestException("flaky");
        };

        await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        await Task.Delay(1100); // let the cache expire so the second call refetches
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.Contains(tools, t => t.Name == "future-tool"); // cached set survives
    }

    private sealed class FakeSecretStore(string? context7Key = null)
        : IIntegrationSecretStore
    {
        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(provider == IntegrationProviders.Context7 ? context7Key : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
