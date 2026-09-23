namespace KnowledgeHub.Server.VectorStore;

/// <summary>
/// Tuning knobs for <see cref="PostgresVectorStore"/> (SPEC-20260923-pgvector-hnsw-scale).
/// Bound from <c>VectorStore:Postgres</c>.
/// </summary>
public sealed class PostgresOptions
{
    public const string SectionName = "VectorStore:Postgres";

    /// <summary>Row count at which the HNSW index is created. Below it the exact
    /// scan is kept (better recall, no index maintenance cost).</summary>
    public int HnswThreshold { get; set; } = 10_000;

    /// <summary>HNSW <c>m</c> parameter (graph degree).</summary>
    public int HnswM { get; set; } = 16;

    /// <summary>HNSW <c>ef_construction</c> parameter.</summary>
    public int HnswEfConstruction { get; set; } = 64;

    /// <summary>Max rows per batched upsert command (5 params per row, Npgsql limit 65535).</summary>
    public int BatchMax { get; set; } = 500;
}
