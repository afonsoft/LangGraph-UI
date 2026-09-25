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

// Covers SPEC-20260924-cloud-storage-connectors RF-006: cloud credentials move
// to the encrypted store (s3:/azure:/oci:), hasKey flag, validation, delete cleanup.
public class CloudSourceServiceTests : IAsyncLifetime
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
    public async Task Create_AwsS3_StoresSecretInSecretStore_NeverInConfig()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "s3",
            Type = SourceType.AwsS3,
            Configuration = Config(("bucketName", "bkt"), ("region", "us-east-1"),
                ("accessKeyId", "AKID"), ("secretAccessKey", "topsecret"))
        });

        Assert.Null(result.Error);
        var dto = result.Value!;

        Assert.Equal("topsecret", _secrets.Store[$"s3:{dto.Id}"]);
        var raw = await _db.Sources.AsNoTracking().FirstAsync(s => s.Id == dto.Id);
        Assert.DoesNotContain("topsecret", raw.ConfigurationJson);
        Assert.True(dto.Configuration!["hasKey"]!.GetValue<bool>());
        Assert.Equal("AKID", dto.Configuration!["accessKeyId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_AwsS3_WithoutSecret_AndNoStored_Fails()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "s3",
            Type = SourceType.AwsS3,
            Configuration = Config(("bucketName", "bkt"), ("region", "us-east-1"), ("accessKeyId", "AKID"))
        });
        Assert.Contains("secretAccessKey", result.Error);
    }

    [Fact]
    public async Task Update_AwsS3_RedactedMarker_KeepsStoredSecret()
    {
        var created = (await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "s3",
            Type = SourceType.AwsS3,
            Configuration = Config(("bucketName", "bkt"), ("region", "us-east-1"),
                ("accessKeyId", "AKID"), ("secretAccessKey", "topsecret"))
        })).Value!;

        var updated = await _svc.UpdateAsync(created.Id, new UpdateKnowledgeSourceRequest
        {
            Name = "s3",
            IsActive = true,
            Configuration = Config(("bucketName", "bkt"), ("region", "us-east-1"),
                ("accessKeyId", "AKID"), ("secretAccessKey", "***"))
        });

        Assert.Null(updated.Error);
        Assert.Equal("topsecret", _secrets.Store[$"s3:{created.Id}"]);
    }

    [Fact]
    public async Task Create_AzureFiles_PacksCredentialsAsJson()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "azure",
            Type = SourceType.AzureFiles,
            Configuration = Config(("shareName", "share"), ("accountKey", "ACCKEY"), ("accountName", "acc"))
        });

        Assert.Null(result.Error);
        var stored = _secrets.Store[$"azure:{result.Value!.Id}"];
        Assert.Contains("ACCKEY", stored);
        Assert.Contains("accountKey", stored);
        Assert.DoesNotContain("ACCKEY", (await _db.Sources.FirstAsync(s => s.Id == result.Value!.Id)).ConfigurationJson);
    }

    [Fact]
    public async Task Create_OciStorage_StoresSecretUnderOciKey()
    {
        var result = await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "oci",
            Type = SourceType.OciStorage,
            Configuration = Config(("namespace", "ns"), ("region", "sa-saopaulo-1"),
                ("bucketName", "bkt"), ("accessKeyId", "AK"), ("secretAccessKey", "ocisecret"))
        });

        Assert.Null(result.Error);
        Assert.Equal("ocisecret", _secrets.Store[$"oci:{result.Value!.Id}"]);
    }

    [Fact]
    public async Task Delete_CloudSource_RemovesSecret()
    {
        var created = (await _svc.CreateAsync(new CreateKnowledgeSourceRequest
        {
            Name = "s3",
            Type = SourceType.AwsS3,
            Configuration = Config(("bucketName", "bkt"), ("region", "us-east-1"),
                ("accessKeyId", "AKID"), ("secretAccessKey", "topsecret"))
        })).Value!;

        await _svc.DeleteAsync(created.Id);
        Assert.False(_secrets.Store.ContainsKey($"s3:{created.Id}"));
    }

    private sealed class FakeNotifier : IToolCatalogChangeNotifier
    {
        public long Version { get; private set; }
        public Task NotifyToolsChangedAsync(CancellationToken ct = default)
        { Version++; return Task.CompletedTask; }
    }
}
