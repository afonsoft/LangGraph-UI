namespace KnowledgeHub.Server.Data;

/// <summary>Effective backend serving the EF catalog — and, by default, the vector
/// store. SPEC-20260926-unified-database-provider RF-001.</summary>
public enum CatalogProvider { Sqlite, Postgres }

/// <summary>
/// Resolves <c>Database:Provider</c> (<c>auto</c>|<c>postgres</c>|<c>sqlite</c>,
/// default <c>auto</c>) into a single effective backend for the whole process.
/// <list type="bullet">
/// <item><c>sqlite</c> — always SQLite, even when Postgres vars exist.</item>
/// <item><c>postgres</c> — requires <c>Database:ConnectionString</c> or the
/// <c>POSTGRES_*</c> env composition; missing config is a startup error.</item>
/// <item><c>auto</c> — Postgres when a connstring resolves AND a short probe
/// connects; otherwise SQLite with <see cref="FallbackReason"/> set.</item>
/// </list>
/// Resolved once per process — the provider never switches at runtime.
/// </summary>
public sealed record CatalogDatabase(
    CatalogProvider Provider,
    string? PostgresConnectionString,
    string? FallbackReason = null)
{
    public bool IsPostgres => Provider == CatalogProvider.Postgres;

    /// <summary>Postgres connstring from <c>Database:ConnectionString</c>, or
    /// composed from the same <c>POSTGRES_*</c> env vars docker-compose already
    /// maps for the vector store. Null when nothing is configured.</summary>
    public static string? ResolvePostgresConnectionString(IConfiguration cfg)
    {
        var direct = cfg.GetValue<string>("Database:ConnectionString");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var host = cfg["POSTGRES_HOST"];
        if (string.IsNullOrWhiteSpace(host))
            return null;
        var csb = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(cfg["POSTGRES_PORT"], out var p) ? p : 5432,
            Database = cfg["POSTGRES_DB"] ?? "rag_db",
            Username = cfg["POSTGRES_USER"] ?? "rag_user",
            Password = cfg["POSTGRES_PASSWORD"] ?? ""
        };
        return csb.ConnectionString;
    }

    /// <summary>Probe used by <c>auto</c> mode — 4s budget so a down Postgres
    /// never stalls startup.</summary>
    private static bool CanReachPostgres(string connectionString, out string? error)
    {
        try
        {
            var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 4,
                Pooling = false
            };
            using var conn = new Npgsql.NpgsqlConnection(csb.ConnectionString);
            conn.Open();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static CatalogDatabase Resolve(IConfiguration cfg)
    {
        var conn = ResolvePostgresConnectionString(cfg);
        var mode = (cfg.GetValue<string>("Database:Provider") ?? "auto").Trim();

        switch (mode.ToLowerInvariant())
        {
            case "sqlite":
                return new CatalogDatabase(CatalogProvider.Sqlite, conn);

            case "postgres":
                return string.IsNullOrWhiteSpace(conn)
                    ? throw new InvalidOperationException(
                        "Database:Provider=postgres requires Database:ConnectionString " +
                        "or the POSTGRES_* env composition (HOST/PORT/DB/USER/PASSWORD).")
                    : new CatalogDatabase(CatalogProvider.Postgres, conn);

            case "auto":
                if (string.IsNullOrWhiteSpace(conn))
                    return new CatalogDatabase(CatalogProvider.Sqlite, null);
                return CanReachPostgres(conn, out var err)
                    ? new CatalogDatabase(CatalogProvider.Postgres, conn)
                    : new CatalogDatabase(CatalogProvider.Sqlite, conn,
                        $"Postgres configured but unreachable — fell back to SQLite: {err}");

            default:
                throw new InvalidOperationException(
                    $"Database:Provider '{mode}' is invalid (expected: auto | postgres | sqlite)");
        }
    }
}
