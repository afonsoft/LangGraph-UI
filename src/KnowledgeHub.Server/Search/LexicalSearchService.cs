using System.Data;
using System.Data.Common;
using KnowledgeHub.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Search;

/// <summary>One FTS5 hit: chunk id + 1-based rank + raw bm25 score.</summary>
public sealed record LexicalHit(Guid ChunkId, int Rank, double Bm25);

/// <summary>
/// SQLite FTS5 lexical index over <c>DocumentChunks</c>
/// (SPEC-20260914-hybrid-retrieval RF-001/RF-004). The <c>chunks_fts</c> virtual
/// table is created and backfilled by the <c>AddChunksFts</c> migration and kept
/// in sync by <see cref="ReconcileAsync"/> on every ingestion write path.
/// </summary>
public interface ILexicalSearchService
{
    /// <summary>False when lexical search is disabled or the vector store is postgres.</summary>
    bool Enabled { get; }

    /// <summary>bm25-ranked chunk ids; empty on syntax/host errors (never throws to callers).</summary>
    Task<IReadOnlyList<LexicalHit>> SearchAsync(
        string query, int topK, IReadOnlyCollection<Guid>? sourceIds, CancellationToken cancellationToken = default);

    /// <summary>Drops FTS rows whose chunk no longer exists and inserts rows for chunks never indexed.</summary>
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}

public sealed class LexicalSearchService(
    KnowledgeHubDbContext db,
    IConfiguration configuration,
    ILogger<LexicalSearchService> logger) : ILexicalSearchService
{
    private const string TableName = "chunks_fts";

    public bool Enabled { get; } =
        configuration.GetValue("Search:Lexical:Enabled", true)
        && !configuration.GetValue("VectorStore:Provider", "sqlite")
            .Equals("postgres", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<LexicalHit>> SearchAsync(
        string query, int topK, IReadOnlyCollection<Guid>? sourceIds, CancellationToken cancellationToken = default)
    {
        var match = FtsQuerySanitizer.ToMatchExpression(query);
        if (match is null || !Enabled || !await IsAvailableAsync(cancellationToken))
            return [];

        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            var sourceFilter = "";
            if (sourceIds is { Count: > 0 })
            {
                var names = new List<string>(sourceIds.Count);
                var i = 0;
                foreach (var id in sourceIds)
                {
                    var name = $"$s{i++}";
                    names.Add(name);
                    AddParameter(command, name, id.ToString());
                }
                // EF Core stores TEXT Guids uppercase; NOCASE makes the filter format-agnostic.
                sourceFilter = $"AND d.KnowledgeSourceId COLLATE NOCASE IN ({string.Join(", ", names)}) ";
            }
            else if (sourceIds is { Count: 0 })
            {
                return [];
            }

            command.CommandText = $"""
                SELECT c.Id, f.rank
                FROM {TableName} f
                JOIN Chunks c ON c.Id = f.chunk_id
                JOIN Documents d ON c.KnowledgeDocumentId = d.Id
                WHERE f.text MATCH $match {sourceFilter}
                ORDER BY f.rank
                LIMIT $limit
                """;
            AddParameter(command, "$match", match);
            AddParameter(command, "$limit", topK);

            var hits = new List<LexicalHit>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(new LexicalHit(
                    Guid.Parse(reader.GetString(0)),
                    hits.Count + 1,
                    reader.GetDouble(1)));
            }
            return hits;
        }
        catch (SqliteException ex)
        {
            // FTS syntax/IO errors degrade to "no lexical hits" — never a 500.
            logger.LogWarning(ex, "Lexical search failed for match expression — falling back to empty result set");
            return [];
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled || !await IsAvailableAsync(cancellationToken))
            return;

        await db.Database.ExecuteSqlRawAsync(
            $"""
            DELETE FROM {TableName}
            WHERE chunk_id NOT IN (SELECT Id FROM Chunks);
            """, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO {TableName} (chunk_id, text)
            SELECT Id, TextContent FROM Chunks
            WHERE Id NOT IN (SELECT chunk_id FROM {TableName});
            """, cancellationToken);
    }

    private async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
            AddParameter(command, "$name", TableName);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
