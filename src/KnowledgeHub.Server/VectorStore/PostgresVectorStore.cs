using Npgsql;
using Pgvector;

namespace KnowledgeHub.Server.VectorStore;

/// <summary>
/// pgvector-backed store (SPEC-02 RF-006). Catalog/documents stay on SQLite —
/// only embeddings live in Postgres (kh_embeddings table), ranked server-side
/// with the &lt;=&gt; cosine-distance operator.
/// </summary>
public sealed class PostgresVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _dimensions;
    private bool _initialized;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    public PostgresVectorStore(string connectionString, int dimensions = 384)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _dataSource = builder.Build();
        _dimensions = dimensions;
    }

    public async Task UpsertAsync(Guid chunkId, Guid documentId, Guid sourceId, float[] vector, string model, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO kh_embeddings (chunk_id, document_id, source_id, model, embedding)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (chunk_id) DO UPDATE SET model = $4, embedding = $5
            """;
        cmd.Parameters.AddWithValue(chunkId);
        cmd.Parameters.AddWithValue(documentId);
        cmd.Parameters.AddWithValue(sourceId);
        cmd.Parameters.AddWithValue(model);
        cmd.Parameters.AddWithValue(new Vector(vector));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteByDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        // chunk ids embed the document id via a companion table keyed by document
        cmd.CommandText = "DELETE FROM kh_embeddings WHERE document_id = $1";
        cmd.Parameters.AddWithValue(documentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] queryVector, string model, int topK,
        IReadOnlyCollection<Guid>? sourceIds = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (sourceIds is { Count: 0 })
            return [];

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        var whereSource = sourceIds is null ? "" : " AND source_id = ANY($4)";
        cmd.CommandText = $"""
            SELECT chunk_id, 1 - (embedding <=> $1) AS score
            FROM kh_embeddings
            WHERE model = $2{whereSource}
            ORDER BY embedding <=> $1
            LIMIT $3
            """;
        cmd.Parameters.AddWithValue(new Vector(queryVector));
        cmd.Parameters.AddWithValue(model);
        cmd.Parameters.AddWithValue(topK);
        if (sourceIds is not null)
            cmd.Parameters.AddWithValue(sourceIds.ToArray());

        var hits = new List<VectorHit>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            hits.Add(new VectorHit(reader.GetGuid(0), reader.GetDouble(1)));
        return hits;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;
        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE EXTENSION IF NOT EXISTS vector;
                CREATE TABLE IF NOT EXISTS kh_embeddings (
                    chunk_id    uuid PRIMARY KEY,
                    document_id uuid NOT NULL,
                    source_id   uuid NOT NULL,
                    model       text NOT NULL,
                    embedding   vector({_dimensions}) NOT NULL
                );
                CREATE INDEX IF NOT EXISTS kh_embeddings_model_idx ON kh_embeddings (model);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _initGate.Dispose();
        await _dataSource.DisposeAsync();
    }
}
