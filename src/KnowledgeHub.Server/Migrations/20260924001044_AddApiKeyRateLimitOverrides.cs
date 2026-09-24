using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyRateLimitOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
