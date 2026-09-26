using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpTasks",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PollIntervalMs = table.Column<long>(type: "INTEGER", nullable: true),
                    TtlMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorJson = table.Column<string>(type: "TEXT", nullable: true),
                    InputRequestsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpTasks", x => x.TaskId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpTasks_Status",
                table: "McpTasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpTasks");
        }
    }
}
