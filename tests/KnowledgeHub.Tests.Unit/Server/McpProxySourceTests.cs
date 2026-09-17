using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Server.Mcp.Upstream;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260917-mcp-proxy-source-type RF-001/RF-003: registration via
// the sources service, apiKey extraction to the encrypted store, validation.
public class McpProxySourceServiceTests : IAsyncLifetime
{
    private SqliteConnection _conn = default!;
    private KnowledgeHubDbContext _db = default!;
    private FakeSecretStore _secrets = default!;
    private KnowledgeSourceService _svc = default!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        _db = new KnowledgeHubDbContext(
            new DbContextOptionsBuilder<KnowledgeHubDbContext>().UseSqlite(_conn).Options);
        await _db.Database.EnsureCreatedAsync();
        _secrets = new FakeSecretStore();
        _svc = new KnowledgeSourceService(_db, new FakeNotifier(), _secrets);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private static JsonObject Config(params (string Key, object? Value)[] pairs)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in pairs)
            obj[k] = v is null ? null : JsonValue.Create(v);
        return obj;
    }

    [Fact]
    public async Task Create_McpProxy_StoresKeyInSecretStore_NeverInConfig()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "Devin",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "https://mcp.devin.ai/mcp"), ("apiKey", "dw-secret-123"))
        });

        Assert.Null(result.Error);
        var dto = result.Value!;

        // apiKey went to the encrypted store under the per-source slug
        Assert.Equal("dw-secret-123", _secrets.Store[$"mcpproxy:{dto.Id}"]);
        // persisted + returned config carries only the hasKey flag
        Assert.False(dto.Configuration!.ContainsKey("apiKey"));
        Assert.True(dto.Configuration["hasKey"]!.GetValue<bool>());
        var stored = _db.Sources.Single(s => s.Id == dto.Id);
        Assert.DoesNotContain("dw-secret-123", stored.ConfigurationJson);
        Assert.DoesNotContain("apiKey", stored.ConfigurationJson);
    }

    [Fact]
    public async Task Create_McpProxy_NoKey_HasKeyFalse()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "pub",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "https://mcp.example.com/mcp"))
        });

        Assert.False(result.Value!.Configuration!["hasKey"]!.GetValue<bool>());
        Assert.Empty(_secrets.Store);
    }

    [Fact]
    public async Task Update_WithoutApiKey_KeepsStoredKey()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "Devin",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "https://mcp.devin.ai/mcp"), ("apiKey", "k1"))
        });
        var id = created.Value!.Id;

        var updated = await _svc.UpdateAsync(id, new UpdateKnowledgeSourceRequest
        {
            Name = "Devin renamed",
            IsActive = true,
            Configuration = Config(("endpoint", "https://mcp.devin.ai/mcp"), ("hasKey", true))
        });

        Assert.Null(updated.Error);
        Assert.Equal("k1", _secrets.Store[$"mcpproxy:{id}"]);
        Assert.True(updated.Value!.Configuration!["hasKey"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Update_EmptyApiKey_RemovesStoredKey()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "Devin",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "https://mcp.devin.ai/mcp"), ("apiKey", "k1"))
        });
        var id = created.Value!.Id;

        await _svc.UpdateAsync(id, new UpdateKnowledgeSourceRequest
        {
            Name = "Devin",
            IsActive = true,
            Configuration = Config(("endpoint", "https://mcp.devin.ai/mcp"), ("apiKey", ""))
        });

        Assert.False(_secrets.Store.ContainsKey($"mcpproxy:{id}"));
    }

    [Fact]
    public async Task Create_McpProxy_InvalidEndpoint_Rejected()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "bad",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "not-a-uri"))
        });
        Assert.Equal(400, result.ErrorStatus);

        var badTransport = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "bad2",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "https://x.example/mcp"), ("transport", "grpc"))
        });
        Assert.Equal(400, badTransport.ErrorStatus);
    }

    [Fact]
    public async Task Delete_McpProxy_RemovesStoredKey()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "Devin",
            Type = SourceType.McpProxy,
            Configuration = Config(("endpoint", "https://mcp.devin.ai/mcp"), ("apiKey", "k1"))
        });
        var id = created.Value!.Id;

        await _svc.DeleteAsync(id);
        Assert.False(_secrets.Store.ContainsKey($"mcpproxy:{id}"));
    }

    internal sealed class FakeSecretStore : IIntegrationSecretStore
    {
        public readonly Dictionary<string, string> Store = new();
        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.TryGetValue(provider, out var v) ? v : (string?)null);
        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);
        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default)
        { Store[provider] = secret; return Task.CompletedTask; }
        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.Remove(provider));
    }

    private sealed class FakeNotifier : IToolCatalogChangeNotifier
    {
        public long Version { get; private set; }
        public Task NotifyToolsChangedAsync(CancellationToken ct = default)
        { Version++; return Task.CompletedTask; }
    }
}

// Covers SPEC-20260917-mcp-proxy-source-type RF-002 / CA-002/CA-003: dynamic
// per-source sessions, slug-prefixed tool names, verbatim schemas, upstream
// failure → IsError, deactivation evicts the session.
public class McpProxyToolsProviderTests : IAsyncLifetime
{
    private SqliteConnection _conn = default!;
    private KnowledgeHubDbContext _db = default!;
    private IServiceProvider _services = default!;
    private McpProxyToolsProvider _provider = default!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        _db = new KnowledgeHubDbContext(
            new DbContextOptionsBuilder<KnowledgeHubDbContext>().UseSqlite(_conn).Options);
        await _db.Database.EnsureCreatedAsync();
        _services = new ServiceCollection().AddSingleton(_db).BuildServiceProvider();
        _provider = new McpProxyToolsProvider(
            new McpProxySourceServiceTests.FakeSecretStore(),
            NullLoggerFactory.Instance,
            NullLogger<McpProxyToolsProvider>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
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

    private async Task<KnowledgeSource> AddSourceAsync(string name, JsonObject config, bool active = true)
    {
        var source = new KnowledgeSource
        {
            Name = name,
            SourceType = SourceType.McpProxy,
            IsActive = active,
            ConfigurationJson = config.ToJsonString()
        };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }

    private static JsonObject Config(params (string Key, object? Value)[] pairs)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in pairs)
            obj[k] = v is null ? null : JsonValue.Create(v);
        return obj;
    }

    [Fact]
    public async Task NoProxySources_ReturnsEmpty()
    {
        var tools = await _provider.GetToolsAsync(_services, CancellationToken.None);
        Assert.Empty(tools);
    }

    [Fact]
    public async Task ActiveSource_ExposesPrefixedUpstreamTools()
    {
        var source = await AddSourceAsync("Devin Cloud",
            Config(("endpoint", "https://mcp.devin.ai/mcp")));
        var session = new FakeSession(source.Id,
            [UpstreamTool("session_create", "create a session", schemaJson: """{"type":"object","properties":{"prompt":{"type":"string"}}}""")]);
        _provider.SessionFactory = _ => session;

        var tools = await _provider.GetToolsAsync(_services, CancellationToken.None);
        var tool = Assert.Single(tools);

        Assert.Equal("devin_cloud_session_create", tool.Name);
        Assert.Equal("create a session", tool.Description);
        Assert.True(tool.InputSchema.ContainsKey("properties"));
        Assert.False(tool.ReadOnly); // no upstream hint → conservative
    }

    [Fact]
    public async Task TwoProxies_SameUpstreamToolName_PrefixAvoidsCollision()
    {
        // Covers CA-003.
        var a = await AddSourceAsync("Alpha", Config(("endpoint", "https://a.example/mcp")));
        var b = await AddSourceAsync("Beta", Config(("endpoint", "https://b.example/mcp")));
        var sessions = new Dictionary<Guid, FakeSession>
        {
            [a.Id] = new FakeSession(a.Id, [UpstreamTool("search")]),
            [b.Id] = new FakeSession(b.Id, [UpstreamTool("search")])
        };
        _provider.SessionFactory = cfg => sessions[cfg.SourceId];

        var names = (await _provider.GetToolsAsync(_services, CancellationToken.None))
            .Select(t => t.Name).ToHashSet();

        Assert.True(names.SetEquals(["alpha_search", "beta_search"]));
    }

    [Fact]
    public async Task EmptyPrefix_UpstreamIdenticalNames()
    {
        var source = await AddSourceAsync("Raw",
            Config(("endpoint", "https://raw.example/mcp"), ("namePrefix", "")));
        _provider.SessionFactory = _ => new FakeSession(source.Id, [UpstreamTool("ping")]);

        var names = (await _provider.GetToolsAsync(_services, CancellationToken.None))
            .Select(t => t.Name);

        Assert.Equal(["ping"], names);
    }

    [Fact]
    public async Task Call_RoutesToSessionWithUpstreamName()
    {
        var source = await AddSourceAsync("Devin", Config(("endpoint", "https://mcp.devin.ai/mcp")));
        var session = new FakeSession(source.Id, [UpstreamTool("session_create")]);
        _provider.SessionFactory = _ => session;

        var tool = (await _provider.GetToolsAsync(_services, CancellationToken.None)).Single();
        var result = await tool.Handler(
            new ToolCallContext
            {
                Services = _services,
                Arguments = new Dictionary<string, JsonElement>
                { ["prompt"] = JsonDocument.Parse("\"hi\"").RootElement }
            },
            CancellationToken.None);

        Assert.Equal("session_create", session.LastCalledTool); // upstream name, not the prefixed one
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task UpstreamDown_ToolsListedBefore_CallReturnsIsError()
    {
        // Covers CA-002: failure propagates as IsError, never crashes.
        var source = await AddSourceAsync("Flaky", Config(("endpoint", "https://flaky.example/mcp")));
        var session = new FakeSession(source.Id, [UpstreamTool("ping")]) { FailCalls = true };
        _provider.SessionFactory = _ => session;

        var tool = (await _provider.GetToolsAsync(_services, CancellationToken.None)).Single();
        var result = await tool.Handler(
            new ToolCallContext { Services = _services }, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvalidEndpoint_SourceSkipped_NoCrash()
    {
        await AddSourceAsync("Bad", Config(("endpoint", "not-a-uri")));
        _provider.SessionFactory = _ => throw new InvalidOperationException("must not be called");

        var tools = await _provider.GetToolsAsync(_services, CancellationToken.None);
        Assert.Empty(tools);
    }

    [Fact]
    public async Task DeactivatedSource_SessionEvicted()
    {
        var source = await AddSourceAsync("Devin", Config(("endpoint", "https://mcp.devin.ai/mcp")));
        var session = new FakeSession(source.Id, [UpstreamTool("ping")]);
        _provider.SessionFactory = _ => session;

        await _provider.GetToolsAsync(_services, CancellationToken.None);

        _db.Sources.Find(source.Id)!.IsActive = false;
        await _db.SaveChangesAsync();

        var tools = await _provider.GetToolsAsync(_services, CancellationToken.None);
        Assert.Empty(tools);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ConfigChange_RebuildsSession()
    {
        var source = await AddSourceAsync("Devin", Config(("endpoint", "https://a.example/mcp")));
        var sessions = new List<FakeSession>();
        _provider.SessionFactory = cfg =>
        {
            var s = new FakeSession(cfg.SourceId, [UpstreamTool("ping")]) { FingerprintOverride = cfg.Fingerprint };
            sessions.Add(s);
            return s;
        };

        await _provider.GetToolsAsync(_services, CancellationToken.None);

        _db.Sources.Find(source.Id)!.ConfigurationJson =
            Config(("endpoint", "https://b.example/mcp")).ToJsonString();
        await _db.SaveChangesAsync();

        await _provider.GetToolsAsync(_services, CancellationToken.None);

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].Disposed);
        Assert.False(sessions[1].Disposed);
    }

    private sealed class FakeSession(Guid sourceId, IReadOnlyList<Tool> tools) : IMcpProxySession
    {
        public string? FingerprintOverride { get; init; }
        public string Fingerprint => FingerprintOverride ?? $"{sourceId}";
        public bool FailCalls { get; init; }
        public bool Disposed { get; private set; }
        public string? LastCalledTool { get; private set; }

        public Task<IReadOnlyList<Tool>> GetToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(tools);

        public ValueTask<CallToolResult> CallAsync(
            string toolName, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
        {
            LastCalledTool = toolName;
            return ValueTask.FromResult(new CallToolResult
            {
                IsError = FailCalls,
                Content = [new TextContentBlock { Text = FailCalls ? "upstream down" : "ok" }]
            });
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
