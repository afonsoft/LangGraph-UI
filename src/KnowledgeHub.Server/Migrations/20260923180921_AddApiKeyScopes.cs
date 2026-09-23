using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                table: "SecurityEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "SecurityEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedSourceIdsJson",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedToolsJson",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                table: "SecurityEvents");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "SecurityEvents");

            migrationBuilder.DropColumn(
                name: "AllowedSourceIdsJson",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "AllowedToolsJson",
                table: "ApiKeys");
        }
    }
}
