using KnowledgeHub.Server.VectorStore;
using Xunit;

namespace KnowledgeHub.Tests.Unit.VectorStore;

/// <summary>
/// SPEC-20260923-pgvector-hnsw-scale — unit tests for the testable seams that do
/// not require a live Postgres: dimension guards, empty-batch no-op, DDL shapes.
/// </summary>
public sealed class PostgresVectorStoreTests
{
    private static PostgresVectorStore NewStore(int dimensions = 384, PostgresOptions? options = null) =>
        new("Host=127.0.0.1;Database=unused", dimensions, options);

    [Fact]
    public async Task UpsertBatch_EmptyList_ReturnsWithoutConnecting()
    {
        // Covers RF-002: empty batch is a no-op — completes without opening a
        // connection (the fake connection string above points at nothing).
        await using var store = NewStore();
        await store.UpsertBatchAsync([], "m");
    }

    [Fact]
    public async Task UpsertAsync_WrongDimensions_ThrowsBeforeConnecting()
    {
        await using var store = NewStore(dimensions: 384);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new float[8], "m"));
        Assert.Contains("384", ex.Message);
    }

    [Fact]
    public async Task UpsertBatch_AnyWrongDimensions_ThrowsBeforeConnecting()
    {
        await using var store = NewStore(dimensions: 384);
        var items = new[]
        {
            new VectorUpsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new float[384]),
            new VectorUpsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new float[8])
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertBatchAsync(items, "m"));
    }

    [Fact]
    public void BuildBatchUpsertSql_GeneratesNRowsOf5Params()
    {
        // Covers RF-002: one multi-row INSERT, 5 positional params per row.
        var sql = PostgresVectorStore.BuildBatchUpsertSql(2);
        Assert.Contains("($1,$2,$3,$4,$5),($6,$7,$8,$9,$10)", sql);
        Assert.Contains("ON CONFLICT (chunk_id) DO UPDATE", sql);
        Assert.Contains("EXCLUDED.model", sql);
    }

    [Fact]
    public void BuildHnswIndexSql_UsesCosineOpsAndOptions()
    {
        // Covers RF-001: HNSW index DDL uses vector_cosine_ops and configured params.
        var store = NewStore(options: new PostgresOptions { HnswM = 24, HnswEfConstruction = 96 });
        var sql = store.BuildHnswIndexSql();
        Assert.Contains("hnsw (embedding vector_cosine_ops)", sql);
        Assert.Contains("m = 24", sql);
        Assert.Contains("ef_construction = 96", sql);
        Assert.Contains(PostgresVectorStore.HnswIndexName, sql);
        Assert.DoesNotContain("CONCURRENTLY", sql);
    }
}
