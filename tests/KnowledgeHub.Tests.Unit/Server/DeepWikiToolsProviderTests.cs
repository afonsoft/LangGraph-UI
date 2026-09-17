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

// Covers SPEC-20260917-upstream-tools-passthrough RF-001..RF-004: static core
// in public mode, dynamic upstream tools/list merge in private mode,
// last-known-good fallback, friendly no-key error on dynamic dispatch.
public class DeepWikiToolsProviderTests
{
    private static readonly IServiceProvider EmptyServices =
        new ServiceCollection().BuildServiceProvider();

    private static (DeepWikiToolsProvider provider, DeepWikiUpstreamClient client, FakeSecretStore store) Build(
        bool enabled = true, string? storedKey = null, string? envKey = null,
        int toolsCacheSeconds = 300)
    {
        var options = new DeepWikiOptions
        {
            Enabled = enabled,
            ApiKey = envKey ?? "",
            ToolsCacheSeconds = toolsCacheSeconds
        };
        var store = new FakeSecretStore(deepwikiKey: storedKey);
        var client = new DeepWikiUpstreamClient(
            Options.Create(options),
            store,
            NullLoggerFactory.Instance,
            NullLogger<DeepWikiUpstreamClient>.Instance);
        var provider = new DeepWikiToolsProvider(
            client, Options.Create(options), NullLogger<DeepWikiToolsProvider>.Instance);
        return (provider, client, store);
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
        var (provider, _, _) = Build(enabled: false);
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.Empty(tools);
    }

    [Fact]
    public async Task Enabled_NoKey_ListsStaticCoreOnly()
    {
        // Covers RF-004: public mode unchanged — 3 fixed tools, no upstream call.
        var (provider, _, _) = Build();
        var names = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Select(t => t.Name).ToHashSet();

        Assert.True(names.SetEquals(
            ["ask_question", "read_wiki_structure", "read_wiki_contents"]));
    }

    [Fact]
    public async Task Enabled_NoKey_DoesNotInvokeDynamicSource()
    {
        var (provider, _, _) = Build();
        var called = false;
        provider.DynamicToolsSource = _ =>
        {
            called = true;
            return Task.FromResult<IReadOnlyList<Tool>>([UpstreamTool("devin_session_create")]);
        };

        await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.False(called);
    }

    [Fact]
    public async Task WithKey_MergesDynamicTools_VerbatimNamesAndSchemas()
    {
        // Covers RF-001: private mode re-exposes upstream tools (devin_*).
        var (provider, _, _) = Build(storedKey: "dw-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("devin_session_create", "create a Devin session",
                schemaJson: """{"type":"object","properties":{"prompt":{"type":"string"}},"required":["prompt"]}"""),
            UpstreamTool("ask_question", "upstream-authoritative ask")
        ]);

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        var byName = tools.ToDictionary(t => t.Name);

        Assert.Contains("devin_session_create", byName.Keys);
        Assert.Equal("create a Devin session", byName["devin_session_create"].Description);
        Assert.Equal("upstream-authoritative ask", byName["ask_question"].Description);
        Assert.True(byName["devin_session_create"].InputSchema.ContainsKey("properties"));
    }

    [Fact]
    public async Task DynamicTool_ReadOnlyHint_Respected_DefaultsNotReadOnly()
    {
        // Covers RF-003: annotations propagate; absent hint → conservative
        // (not read-only) so the Playground keeps the write-confirm gate.
        var (provider, _, _) = Build(storedKey: "dw-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
        [
            UpstreamTool("devin_session_search", readOnly: true),
            UpstreamTool("devin_session_create") // no hint → not read-only
        ]);

        var tools = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .ToDictionary(t => t.Name);

        Assert.True(tools["devin_session_search"].ReadOnly);
        Assert.False(tools["devin_session_create"].ReadOnly);
    }

    [Fact]
    public async Task DynamicListFailure_FallsBackToStaticCore()
    {
        // Covers AC-03: discovery failure keeps public-mode tool set.
        var (provider, _, _) = Build(storedKey: "dw-test");
        provider.DynamicToolsSource = _ => throw new HttpRequestException("upstream down");

        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t.Name == "ask_question");
    }

    [Fact]
    public async Task DynamicListFailure_KeepsLastKnownGood()
    {
        var (provider, _, _) = Build(storedKey: "dw-test", toolsCacheSeconds: 1);
        var calls = 0;
        provider.DynamicToolsSource = _ =>
        {
            calls++;
            if (calls == 1)
                return Task.FromResult<IReadOnlyList<Tool>>([UpstreamTool("devin_session_create")]);
            throw new HttpRequestException("flaky");
        };

        await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        await Task.Delay(1100); // let the cache expire so the second call refetches
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.Contains(tools, t => t.Name == "devin_session_create");
    }

    [Fact]
    public async Task InvalidateToolsCache_Refetches()
    {
        // Covers RF-005: Settings save/remove drops the cached upstream set.
        var (provider, _, _) = Build(storedKey: "dw-test");
        var calls = 0;
        provider.DynamicToolsSource = _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Tool>>([UpstreamTool($"devin_tool_{calls}")]);
        };

        await provider.GetToolsAsync(EmptyServices, CancellationToken.None);
        provider.InvalidateToolsCache();
        var tools = await provider.GetToolsAsync(EmptyServices, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Contains(tools, t => t.Name == "devin_tool_2");
    }

    [Fact]
    public async Task DynamicTool_KeyRemoved_ReturnsFriendlyIsError()
    {
        // Covers RF-004: a dynamic tool invoked after key removal returns a
        // friendly isError instead of hitting the public upstream.
        var (provider, _, store) = Build(storedKey: "dw-test");
        provider.DynamicToolsSource = _ => Task.FromResult<IReadOnlyList<Tool>>(
            [UpstreamTool("devin_session_create")]);

        var tool = (await provider.GetToolsAsync(EmptyServices, CancellationToken.None))
            .Single(t => t.Name == "devin_session_create");

        store.DeepWikiKey = null;
        var result = await tool.Handler(
            new ToolCallContext { Services = EmptyServices }, CancellationToken.None);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("DeepWiki API key", text);
        Assert.DoesNotContain("dw-test", text); // never echoes key material
    }

    private sealed class FakeSecretStore : IIntegrationSecretStore
    {
        public string? DeepWikiKey { get; set; }
        public FakeSecretStore(string? deepwikiKey = null) => DeepWikiKey = deepwikiKey;

        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(provider == IntegrationProviders.DeepWiki ? DeepWikiKey : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
