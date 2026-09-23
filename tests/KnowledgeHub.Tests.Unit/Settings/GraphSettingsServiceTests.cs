using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeHub.Tests.Unit.Settings;

/// <summary>
/// SPEC-20260923-graph-settings-ui RF-002/RF-005: snapshot resolution
/// (store → env → defaults), invalidation, and catalog notification on
/// save/clear (the Enabled flag gates tool visibility).
/// </summary>
public sealed class GraphSettingsServiceTests : IDisposable
{
    private sealed class FakeNotifier : IToolCatalogChangeNotifier
    {
        public long Version { get; private set; }
        public Task NotifyToolsChangedAsync(CancellationToken cancellationToken = default)
        {
            Version++;
            return Task.CompletedTask;
        }
    }

    private readonly Microsoft.Data.Sqlite.SqliteConnection _conn;
    private readonly ServiceProvider _provider;
    private readonly FakeNotifier _notifier = new();

    public GraphSettingsServiceTests()
    {
        _conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var services = new ServiceCollection();
        services.AddDbContext<KnowledgeHubDbContext>(o => o.UseSqlite(_conn));
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    private GraphSettingsService Sut(IConfiguration cfg) => new(
        cfg,
        _provider.GetRequiredService<IServiceScopeFactory>(),
        _notifier,
        NullLogger<GraphSettingsService>.Instance);

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static readonly SaveGraphSettingsRequest StoreRequest = new()
    {
        Enabled = true,
        MaxChunksPerSync = 50,
        MaxChunkChars = 1000,
        MaxResults = 100
    };

    [Fact]
    public void Effective_Defaults_WhenNoRowAndNoEnv()
    {
        var sut = Sut(Config(new()));
        var snap = sut.GetEffective();
        Assert.False(snap.Enabled);
        Assert.Equal(200, snap.MaxChunksPerSync);
        Assert.Equal(2000, snap.MaxChunkChars);
        Assert.Equal(200, snap.MaxResults);
        Assert.Equal("env", snap.Source);
    }

    [Fact]
    public void Effective_EnvValues_WhenNoRow()
    {
        var sut = Sut(Config(new()
        {
            ["Graph:Enabled"] = "true",
            ["Graph:MaxChunksPerSync"] = "42",
            ["Graph:MaxResults"] = "77"
        }));
        var snap = sut.GetEffective();
        Assert.True(snap.Enabled);
        Assert.Equal(42, snap.MaxChunksPerSync);
        Assert.Equal(2000, snap.MaxChunkChars); // unset key → default
        Assert.Equal(77, snap.MaxResults);
    }

    [Fact]
    public async Task Save_StoreRow_OverridesEnv_AndNotifiesCatalog()
    {
        var sut = Sut(Config(new() { ["Graph:Enabled"] = "false" }));
        await sut.SaveAsync(StoreRequest);

        var snap = sut.GetEffective();
        Assert.True(snap.Enabled);
        Assert.Equal(50, snap.MaxChunksPerSync);
        Assert.Equal(1000, snap.MaxChunkChars);
        Assert.Equal(100, snap.MaxResults);
        Assert.Equal("store", snap.Source);
        Assert.Equal(1, _notifier.Version);
    }

    [Fact]
    public async Task Describe_ReflectsStoreValues_AndUpdatedAt()
    {
        var sut = Sut(Config(new()));
        await sut.SaveAsync(StoreRequest);

        var dto = await sut.DescribeAsync();
        Assert.True(dto.Enabled);
        Assert.Equal("store", dto.Source);
        Assert.NotNull(dto.UpdatedAt);
        Assert.False(dto.EnvConfigured);
    }

    [Fact]
    public async Task Clear_RemovesRow_FallsBackToEnv_AndNotifiesCatalog()
    {
        var sut = Sut(Config(new() { ["Graph:MaxResults"] = "55" }));
        await sut.SaveAsync(StoreRequest);
        Assert.Equal(1, _notifier.Version);

        await sut.ClearAsync();
        var snap = sut.GetEffective();
        Assert.False(snap.Enabled);
        Assert.Equal(55, snap.MaxResults); // env again
        Assert.Equal("env", snap.Source);
        Assert.Equal(2, _notifier.Version);
    }

    [Fact]
    public async Task Invalidate_Reloads_FromStore()
    {
        var sut = Sut(Config(new()));
        await sut.SaveAsync(StoreRequest);
        Assert.True(sut.GetEffective().Enabled);

        // External writer flips the row — cached snapshot must not see it
        // until Invalidate() is called.
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            var row = await db.GraphSettings.SingleAsync();
            row.Enabled = false;
            await db.SaveChangesAsync();
        }
        Assert.True(sut.GetEffective().Enabled); // still cached
        sut.Invalidate();
        Assert.False(sut.GetEffective().Enabled); // reloaded
    }
}
