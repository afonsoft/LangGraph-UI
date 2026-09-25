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

    /// <summary>SPEC-20260925-pgvector-source-cascade RF-003: run ANALYZE after
    /// batch upserts at or above this row count.</summary>
    public int AnalyzeThresholdRows { get; set; } = 500;

    /// <summary>SPEC-20260925-pgvector-iterative-filtered-scan RF-001: opt-in
    /// <c>pgvector.iterative_scan=relaxed_order</c> — HNSW keeps scanning until
    /// topK rows survive the <c>source_id</c> filter (pgvector ≥0.8; the SET is
    /// ignored on older versions). Max distance checks before giving up.</summary>
    public bool IterativeScan { get; set; }

    /// <summary>Companion knob for <see cref="IterativeScan"/> —
    /// <c>hnsw.max_scan_tuples</c> (0 = pgvector default 20000).</summary>
    public int IterativeScanMaxTuples { get; set; }

    /// <summary>SPEC-20260925-pgvector-halfvec RF-001: column/ops flavour —
    /// <c>"vector"</c> (default) or <c>"halfvec"</c> (float16, ~2x smaller,
    /// pgvector ≥0.7, dims ≤2000). Opt-in — switching on an existing table
    /// requires <see cref="AllowStorageMigration"/>.</summary>
    public string StorageType { get; set; } = "vector";

    /// <summary>SPEC-20260925-pgvector-halfvec RF-003: consent gate for the
    /// table-rewriting <c>ALTER COLUMN … TYPE halfvec</c>. Without it a
    /// mismatched column is a startup warning, never a silent rewrite.</summary>
    public bool AllowStorageMigration { get; set; }
}
