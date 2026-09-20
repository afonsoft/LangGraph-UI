using System.Text.Json.Nodes;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260919-notion-connector RF-005/RF-007: token extraction to the
// encrypted store under `notion:{sourceId}`, hasKey flag, validation, delete.
public class NotionSourceServiceTests : IAsyncLifetime
{
    private SqliteConnection _conn = default!;
    private KnowledgeHubDbContext _db = default!;
    private McpProxySourceServiceTests.FakeSecretStore _secrets = default!;
    private KnowledgeSourceService _svc = default!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        _db = new KnowledgeHubDbContext(
            new DbContextOptionsBuilder<KnowledgeHubDbContext>().UseSqlite(_conn).Options);
        await _db.Database.EnsureCreatedAsync();
        _secrets = new McpProxySourceServiceTests.FakeSecretStore();
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
    public async Task Create_Notion_StoresTokenInSecretStore_NeverInConfig()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "notion",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_secret_123"), ("maxPages", 50))
        });

        Assert.Null(result.Error);
        var dto = result.Value!;

        Assert.Equal("ntn_secret_123", _secrets.Store[$"notion:{dto.Id}"]);
        Assert.False(dto.Configuration!.ContainsKey("token"));
        Assert.True(dto.Configuration["hasKey"]!.GetValue<bool>());
        var stored = _db.Sources.Single(s => s.Id == dto.Id);
        Assert.DoesNotContain("ntn_secret_123", stored.ConfigurationJson);
        Assert.DoesNotContain("token", stored.ConfigurationJson);
    }

    [Fact]
    public async Task Create_Notion_NoTokenNoHasKey_Rejected()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "bad",
            Type = SourceType.Notion,
            Configuration = Config(("maxPages", 50))
        });

        Assert.Equal(400, result.ErrorStatus);
        Assert.Contains("token", result.Error!);
    }

    [Fact]
    public async Task Create_Notion_InvalidApiBaseUrl_Rejected()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "bad",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_x"), ("apiBaseUrl", "not-a-uri"))
        });
        Assert.Equal(400, result.ErrorStatus);
    }

    [Fact]
    public async Task Create_Notion_InvalidMaxPages_Rejected()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "bad",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_x"), ("maxPages", 0))
        });
        Assert.Equal(400, result.ErrorStatus);
    }

    [Fact]
    public async Task Update_WithoutToken_KeepsStoredToken()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "notion",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_k1"))
        });
        var id = created.Value!.Id;

        var updated = await _svc.UpdateAsync(id, new UpdateKnowledgeSourceRequest
        {
            Name = "notion renamed",
            IsActive = true,
            Configuration = Config(("hasKey", true), ("rootPageIds", new[] { "p1", "p2" }))
        });

        Assert.Null(updated.Error);
        Assert.Equal("ntn_k1", _secrets.Store[$"notion:{id}"]);
        Assert.True(updated.Value!.Configuration!["hasKey"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Update_EmptyToken_RemovesStoredToken()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "notion",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_k1"))
        });
        var id = created.Value!.Id;

        await _svc.UpdateAsync(id, new UpdateKnowledgeSourceRequest
        {
            Name = "notion",
            IsActive = true,
            Configuration = Config(("token", ""))
        });

        Assert.False(_secrets.Store.ContainsKey($"notion:{id}"));
        var stored = _db.Sources.Single(s => s.Id == id);
        Assert.Contains("\"hasKey\":false", stored.ConfigurationJson!.Replace(" ", ""));
    }

    [Fact]
    public async Task Update_RedactedToken_KeepsStoredToken()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "notion",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_k1"))
        });
        var id = created.Value!.Id;

        await _svc.UpdateAsync(id, new UpdateKnowledgeSourceRequest
        {
            Name = "notion",
            IsActive = true,
            Configuration = Config(("token", "***"))
        });

        Assert.Equal("ntn_k1", _secrets.Store[$"notion:{id}"]);
    }

    [Fact]
    public async Task Create_Notion_HasKeyWithoutSecret_Rejected()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "bad",
            Type = SourceType.Notion,
            Configuration = Config(("hasKey", true))
        });
        Assert.Equal(400, result.ErrorStatus);
        Assert.Contains("token", result.Error!);
    }

    [Fact]
    public async Task Update_HasKeyWithoutSecret_Rejected()
    {
        // Source exists but its token was wiped from the store — a bare
        // hasKey:true update must fail loudly instead of saving a dead source.
        var source = new KnowledgeSource
        {
            Name = "notion",
            SourceType = SourceType.Notion,
            IsActive = true,
            ConfigurationJson = """{"hasKey":true}"""
        };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync();

        var result = await _svc.UpdateAsync(source.Id, new UpdateKnowledgeSourceRequest
        {
            Name = "notion",
            IsActive = true,
            Configuration = Config(("hasKey", true))
        });

        Assert.Equal(400, result.ErrorStatus);
        Assert.Contains("token", result.Error!);
    }

    [Fact]
    public async Task Delete_Notion_RemovesStoredToken()
    {
        var created = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "notion",
            Type = SourceType.Notion,
            Configuration = Config(("token", "ntn_k1"))
        });
        var id = created.Value!.Id;

        await _svc.DeleteAsync(id);
        Assert.False(_secrets.Store.ContainsKey($"notion:{id}"));
    }

    private sealed class FakeNotifier : IToolCatalogChangeNotifier
    {
        public long Version { get; private set; }
        public Task NotifyToolsChangedAsync(CancellationToken ct = default)
        { Version++; return Task.CompletedTask; }
    }
}
