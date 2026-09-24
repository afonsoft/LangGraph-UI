using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class ChunkContextEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnrichedText",
                table: "Chunks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SectionPath",
                table: "Chunks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LlmRateLimitPermits",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LlmRateLimitWindowSeconds",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncRateLimitPermits",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncRateLimitWindowSeconds",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnrichedText",
                table: "Chunks");

            migrationBuilder.DropColumn(
                name: "SectionPath",
                table: "Chunks");

            migrationBuilder.DropColumn(
                name: "LlmRateLimitPermits",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "LlmRateLimitWindowSeconds",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "SyncRateLimitPermits",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "SyncRateLimitWindowSeconds",
                table: "ApiKeys");
        }
    }
}
