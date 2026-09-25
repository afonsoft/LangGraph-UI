using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace KnowledgeHub.Server.Data;

/// <summary>
/// SPEC-20260926-search-correctness-and-stream RF-001: the scoped
/// <see cref="KnowledgeHubDbContext"/> shares ONE <see cref="SqliteConnection"/>
/// which cannot serve concurrent readers — query expansion issues N
/// simultaneous calls. Prefer a dedicated clone of the connection (file
/// databases); fall back to a per-connection gate when cloning is impossible
/// (<c>:memory:</c> databases are private per connection).
/// </summary>
internal static class SqliteConnectionLease
{
    private static readonly ConditionalWeakTable<DbConnection, SemaphoreSlim> Gates = new();

    /// <summary>New, independent connection for the same database — null when
    /// the data source cannot be shared across connections (private-cache
    /// in-memory databases).</summary>
    public static SqliteConnection? Dedicated(DbConnection efConnection)
    {
        if (efConnection is not SqliteConnection sqlite)
            return null;
        var builder = new SqliteConnectionStringBuilder(sqlite.ConnectionString);
        if (builder.DataSource is "" or ":memory:")
            return null;
        return new SqliteConnection(sqlite.ConnectionString);
    }

    /// <summary>Serializes callers on the shared scoped connection — the
    /// fallback path when a dedicated clone is impossible.</summary>
    public static SemaphoreSlim GateFor(DbConnection connection) =>
        Gates.GetValue(connection, _ => new SemaphoreSlim(1, 1));
}
