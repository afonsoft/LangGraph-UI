namespace KnowledgeHub.Shared.Contracts;

/// <summary>
/// SPEC-20260926-settings-tabs-database-metrics RF-002: storage snapshot for the
/// "Banco de Dados" settings tab — EF provider, file/WAL sizes, SQLite PRAGMAs,
/// per-entity row counts, migrations and the vector store diagnostics payload.
/// Fields are null when the provider isn't local SQLite (fail-soft per RF-002).
/// </summary>
public sealed class DatabaseStatsDto
{
    public string Provider { get; set; } = "";
    public string? DataSource { get; set; }
    public long? FileSizeBytes { get; set; }
    public long? WalSizeBytes { get; set; }
    public long? PageCount { get; set; }
    public long? PageSizeBytes { get; set; }
    public long? FreelistCount { get; set; }
    public long? CacheSizePages { get; set; }
    public int MigrationsApplied { get; set; }
    public string? LastMigration { get; set; }
    public IReadOnlyList<TableCountDto> Tables { get; set; } = [];
    public object? VectorStore { get; set; }
}

public sealed class TableCountDto
{
    public string Name { get; set; } = "";
    public long RowCount { get; set; }
}
