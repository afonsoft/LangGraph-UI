using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class EvalBaselinesAndGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaselineName",
                table: "EvalRuns",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateResultJson",
                table: "EvalRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatencyJson",
                table: "EvalRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvalBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EvalRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvalBaselines_EvalRuns_EvalRunId",
                        column: x => x.EvalRunId,
                        principalTable: "EvalRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvalBaselines_EvalRunId",
                table: "EvalBaselines",
                column: "EvalRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalBaselines_Name",
                table: "EvalBaselines",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvalBaselines");

            migrationBuilder.DropColumn(
                name: "BaselineName",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "GateResultJson",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "LatencyJson",
                table: "EvalRuns");
        }
    }
}
