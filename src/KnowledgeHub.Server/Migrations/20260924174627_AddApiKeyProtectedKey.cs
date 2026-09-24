using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyProtectedKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedKey",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectedKey",
                table: "ApiKeys");
        }
    }
}
