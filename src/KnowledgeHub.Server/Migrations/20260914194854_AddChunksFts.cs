using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChunksFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SPEC-20260914-hybrid-retrieval RF-001: unmapped FTS5 index over
            // Chunks.TextContent. unicode61 + remove_diacritics 2 for pt-BR.
            // chunk_id stores the chunk's TEXT Guid; kept in sync by explicit
            // writes (LexicalSearchService.ReconcileAsync) on every write path.
            migrationBuilder.Sql(
                """
                CREATE VIRTUAL TABLE chunks_fts USING fts5(
                    chunk_id UNINDEXED,
                    text,
                    tokenize='unicode61 remove_diacritics 2'
                );
                """);

            // Backfill pre-existing chunks idempotently.
            migrationBuilder.Sql(
                """
                INSERT INTO chunks_fts (chunk_id, text)
                SELECT Id, TextContent FROM Chunks
                WHERE Id NOT IN (SELECT chunk_id FROM chunks_fts);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS chunks_fts;");
        }
    }
}
