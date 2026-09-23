using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChunkKind",
                table: "Chunks",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "markdown");

            migrationBuilder.AddColumn<string>(
                name: "SymbolPath",
                table: "Chunks",
                type: "TEXT",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChunkKind",
                table: "Chunks");

            migrationBuilder.DropColumn(
                name: "SymbolPath",
                table: "Chunks");
        }
    }
}
