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
            Guid.NewGuid(), docId, Guid.NewGuid(), new[] { 1f, i, 0f, 0f },
            new Dictionary<string, string>
            {
                ["sourceType"] = "WebPage",
                ["indexedAt"] = "2026-09-24T00:00:00.0000000Z"
            })).ToList();

        await store.UpsertBatchAsync(items, "test-model");

        // RF-001: threshold crossed (5 >= 3) → index must exist.
        await using var conn = new Npgsql.NpgsqlConnection(ConnString);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT count(*) FROM pg_indexes WHERE indexname = $1";
        check.Parameters.AddWithValue("kh_embeddings_embedding_hnsw_idx");
        Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);

        // SPEC-20260923-pgvector-metadata-upsert AC: rows carry the map.
        await using var metaCheck = conn.CreateCommand();
        metaCheck.CommandText =
            "SELECT count(*) FROM kh_embeddings WHERE document_id = $1" +
            " AND metadata->>'sourceType' = 'WebPage'";
        metaCheck.Parameters.AddWithValue(docId);
        Assert.Equal(5L, (long)(await metaCheck.ExecuteScalarAsync())!);

        var hits = await store.SearchAsync(new[] { 1f, 0f, 0f, 0f }, "test-model", 3);
        Assert.NotEmpty(hits);

        await store.DeleteByDocumentAsync(docId);
    }

    /// <summary>SPEC-20260926-pgvector-scan-and-halfvec RF-003: on a fresh
    /// database the extension must exist before storage resolution — a
    /// halfvec opt-in must actually produce a halfvec column.</summary>
    [Fact]
    public async Task FreshDb_StorageTypeHalfvec_ProducesHalfvecColumn()
    {
        if (string.IsNullOrWhiteSpace(ConnString))
            return; // gated: docker run pgvector/pgvector:pg16

        await using var store = new PostgresVectorStore(ConnString, dimensions: 4,
            new PostgresOptions { StorageType = "halfvec", AllowStorageMigration = true });
        await store.UpsertBatchAsync(
            [new VectorUpsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new[] { 1f, 0f, 0f, 0f })],
            "test-model");

        await using var conn = new Npgsql.NpgsqlConnection(ConnString);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT udt_name FROM information_schema.columns WHERE table_name='kh_embeddings' AND column_name='embedding'";
        Assert.Equal("halfvec", await check.ExecuteScalarAsync() as string);
    }

    /// <summary>SPEC-20260926-pgvector-scan-and-halfvec RF-002: with an HNSW
    /// index present, migration drops it BEFORE altering the column type —
    /// the ALTER would otherwise fail on the vector ops class.</summary>
    [Fact]
    public async Task HalfvecMigration_WithExistingHnsw_Completes()
    {
        if (string.IsNullOrWhiteSpace(ConnString))
            return; // gated: docker run pgvector/pgvector:pg16

        // 1) plain vector store with index created past threshold
        await using (var store = new PostgresVectorStore(ConnString, 4,
            new PostgresOptions { HnswThreshold = 1 }))
        {
            await store.UpsertBatchAsync(
                [new VectorUpsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new[] { 1f, 0f, 0f, 0f }),
                 new VectorUpsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new[] { 0f, 1f, 0f, 0f })],
                "test-model");
        }

        // 2) same table, halfvec opt-in with migration allowed → must not fail
        await using (var store = new PostgresVectorStore(ConnString, 4,
            new PostgresOptions { StorageType = "halfvec", AllowStorageMigration = true, HnswThreshold = 1 }))
        {
            await store.UpsertBatchAsync(
                [new VectorUpsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new[] { 0f, 0f, 1f, 0f })],
                "test-model");
        }

        await using var conn = new Npgsql.NpgsqlConnection(ConnString);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT udt_name FROM information_schema.columns WHERE table_name='kh_embeddings' AND column_name='embedding'";
        Assert.Equal("halfvec", await check.ExecuteScalarAsync() as string);

        // RF-503: the HNSW index must come back with the halfvec ops class —
        // verifying the column alone would pass while the index is missing.
        await using var idx = conn.CreateCommand();
        idx.CommandText =
            "SELECT indexdef FROM pg_indexes WHERE indexname='kh_embeddings_embedding_hnsw_idx'";
        var def = await idx.ExecuteScalarAsync() as string;
        Assert.NotNull(def);
        Assert.Contains("halfvec_cosine_ops", def);
    }
}
