namespace KnowledgeHub.Server.VectorStore;

/// <summary>
/// Tuning knobs for <see cref="PostgresVectorStore"/> (SPEC-20260923-pgvector-hnsw-scale).
/// Bound from <c>VectorStore:Postgres</c>.
/// </summary>
public sealed class PostgresOptions
{
    public const string SectionName = "VectorStore:Postgres";

    /// <summary>Row count at which the HNSW index is created. Below it the exact
    /// scan is kept (better recall, no index maintenance cost).
    /// Default reduced to 1000 for faster query performance (SPEC-20260924-pgvector-rag-performance RF-002).</summary>
    public int HnswThreshold { get; set; } = 1_000;

    /// <summary>HNSW <c>m</c> parameter (graph degree).</summary>
    public int HnswM { get; set; } = 16;

    /// <summary>HNSW <c>ef_construction</c> parameter.</summary>
    public int HnswEfConstruction { get; set; } = 64;

    /// <summary>HNSW <c>ef_search</c> parameter during query execution
    /// (SPEC-20260924-pgvector-rag-performance RF-003).</summary>
    public int HnswEfSearch { get; set; } = 40;

    /// <summary>Max rows per batched upsert command (5 params per row, Npgsql limit 65535).</summary>
    public int BatchMax { get; set; } = 500;

    /// <summary>Minimum connection pool size (SPEC-20260924-pgvector-rag-performance RF-004).</summary>
    public int MinPoolSize { get; set; } = 5;

    /// <summary>Maximum connection pool size (SPEC-20260924-pgvector-rag-performance RF-004).</summary>
    public int MaxPoolSize { get; set; } = 50;

    /// <summary>Idle connection lifetime in seconds before closing.</summary>
    public int ConnectionIdleLifetimeSeconds { get; set; } = 300;

    /// <summary>Command timeout in seconds for vector queries.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
