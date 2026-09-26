using KnowledgeHub.Server.VectorStore;
using Testcontainers.PostgreSql;
using Xunit;

namespace KnowledgeHub.Tests.Integration;

/// <summary>
/// SPEC-20260926-pgvector-live-tests — PostgresVectorStore exercised against a real
/// pgvector instance via Testcontainers. When Docker is unavailable the fixture stays
/// <see cref="PgVectorFixture.Available"/>=false and every test returns early (same
/// convention as the env-gated <see cref="PostgresVectorStoreTests"/>).
/// </summary>
public sealed class PgVectorFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? ConnectionString { get; private set; }
    public bool Available => ConnectionString is not null;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch
        {
            // No Docker daemon (or pull failed) — live tests skip silently.
            _container = null;
            ConnectionString = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

public sealed class PostgresVectorStoreLiveTests : IClassFixture<PgVectorFixture>
{
    private const int Dims = 4;
    private readonly PgVectorFixture _fixture;

    public PostgresVectorStoreLiveTests(PgVectorFixture fixture) => _fixture = fixture;

    private PostgresVectorStore NewStore() =>
        new(_fixture.ConnectionString!, Dims, new PostgresOptions { HnswThreshold = 1_000_000 });

    private static VectorUpsert Vec(Guid doc, Guid src, float x, float y, string? marker = null) =>
        new(Guid.NewGuid(), doc, src, new[] { x, y, 0f, 0f },
            marker is null ? null : new Dictionary<string, string> { ["tag"] = marker });

    // RF-002: full roundtrip — upsert, then cosine search returns the nearest chunk
    // sorted by score through a live pgvector connection.
    [Fact]
    public async Task Upsert_Search_Roundtrip_ReturnsNearest()
    {
        if (!_fixture.Available) return;
        await using var store = NewStore();
        var src = Guid.NewGuid();
        var doc = Guid.NewGuid();

        var near = Vec(doc, src, 1f, 0f);
        var far = Vec(doc, src, 0f, 1f);
        await store.UpsertBatchAsync(new[] { near, far }, "live-model");

        var hits = await store.SearchAsync(new[] { 1f, 0.01f, 0f, 0f }, "live-model", topK: 2);

        Assert.Equal(2, hits.Count);
        Assert.Equal(near.ChunkId, hits[0].ChunkId);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    // RF-002: regression for commit 4aa88d4 — SearchAsync must dispose the reader
    // before CommitAsync or Npgsql 10 throws NpgsqlOperationInProgressException.
    [Fact]
    public async Task Search_CompletesTransactionWithoutOpenReaderError()
    {
        if (!_fixture.Available) return;
        await using var store = NewStore();
        var src = Guid.NewGuid();
        await store.UpsertBatchAsync(new[] { Vec(Guid.NewGuid(), src, 1f, 0f) }, "tx-model");

        // Multiple sequential searches force repeated open-reader/commit cycles.
        for (var i = 0; i < 3; i++)
        {
            var hits = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "tx-model", topK: 5);
            Assert.Single(hits);
        }
    }

    // RF-002: selective delete — only vectors of the target source are removed.
    [Fact]
    public async Task DeleteBySource_RemovesOnlyThatSource()
    {
        if (!_fixture.Available) return;
        await using var store = NewStore();
        var srcA = Guid.NewGuid();
        var srcB = Guid.NewGuid();
        await store.UpsertBatchAsync(
            new[] { Vec(Guid.NewGuid(), srcA, 1f, 0f), Vec(Guid.NewGuid(), srcB, 0.9f, 0.1f) },
            "del-model");

        await store.DeleteBySourceAsync(srcA);

        var hitsA = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "del-model", 10, new[] { srcA });
        var hitsB = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "del-model", 10, new[] { srcB });
        Assert.Empty(hitsA);
        Assert.Single(hitsB);
    }

    // RF-002: delete by document leaves sibling documents of the same source intact.
    [Fact]
    public async Task DeleteByDocument_RemovesOnlyThatDocument()
    {
        if (!_fixture.Available) return;
        await using var store = NewStore();
        var src = Guid.NewGuid();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var keep = Vec(docB, src, 1f, 0f);
        await store.UpsertBatchAsync(new[] { Vec(docA, src, 1f, 0f), keep }, "doc-model");

        await store.DeleteByDocumentAsync(docA);

        var hits = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "doc-model", 10, new[] { src });
        Assert.Single(hits);
        Assert.Equal(keep.ChunkId, hits[0].ChunkId);
    }

    // RF-002: model filter — vectors persisted under another model are invisible.
    [Fact]
    public async Task Search_FiltersByModel()
    {
        if (!_fixture.Available) return;
        await using var store = NewStore();
        var src = Guid.NewGuid();
        await store.UpsertBatchAsync(new[] { Vec(Guid.NewGuid(), src, 1f, 0f) }, "model-a");
        await store.UpsertBatchAsync(new[] { Vec(Guid.NewGuid(), src, 1f, 0f) }, "model-b");

        var hits = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "model-a", 10, new[] { src });

        Assert.Single(hits);
    }
}
