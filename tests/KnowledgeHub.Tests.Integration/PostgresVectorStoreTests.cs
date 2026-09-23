using KnowledgeHub.Server.VectorStore;
using Xunit;

namespace KnowledgeHub.Tests.Integration;

/// <summary>
/// SPEC-20260923-pgvector-hnsw-scale — live Postgres verification. Runs only when
/// <c>KH_TEST_PG</c> points at a pgvector-capable connection string; otherwise
/// the test exits early (no xunit skip-dependency in this project).
/// </summary>
public sealed class PostgresVectorStoreTests
{
    private static string? ConnString => Environment.GetEnvironmentVariable("KH_TEST_PG");

    [Fact]
    public async Task BatchUpsert_And_HnswIndex_WorkAgainstLivePostgres()
    {
        if (string.IsNullOrWhiteSpace(ConnString))
            return; // gated: docker run pgvector/pgvector:pg16

        var options = new PostgresOptions { HnswThreshold = 3 };
        await using var store = new PostgresVectorStore(ConnString, dimensions: 4, options);

        var docId = Guid.NewGuid();
        var items = Enumerable.Range(0, 5).Select(i => new VectorUpsert(
            Guid.NewGuid(), docId, Guid.NewGuid(), new[] { 1f, i, 0f, 0f })).ToList();

        await store.UpsertBatchAsync(items, "test-model");

        // RF-001: threshold crossed (5 >= 3) → index must exist.
        await using var conn = new Npgsql.NpgsqlConnection(ConnString);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT count(*) FROM pg_indexes WHERE indexname = $1";
        check.Parameters.AddWithValue("kh_embeddings_embedding_hnsw_idx");
        Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);

        var hits = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "test-model", 3);
        Assert.NotEmpty(hits);

        await store.DeleteByDocumentAsync(docId);
    }
}
