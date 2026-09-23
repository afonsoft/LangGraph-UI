using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KgNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KgNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KgAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AliasNormalized = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    KgNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KgAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KgAliases_KgNodes_KgNodeId",
                        column: x => x.KgNodeId,
                        principalTable: "KgNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KgEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EvidenceChunkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KgEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KgEdges_Documents_KnowledgeDocumentId",
                        column: x => x.KnowledgeDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KgEdges_KgNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "KgNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KgEdges_KgNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "KgNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KgAliases_AliasNormalized",
                table: "KgAliases",
                column: "AliasNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_KgAliases_AliasNormalized_KgNodeId",
                table: "KgAliases",
                columns: new[] { "AliasNormalized", "KgNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KgAliases_KgNodeId",
                table: "KgAliases",
                column: "KgNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_KgEdges_EvidenceChunkId",
                table: "KgEdges",
                column: "EvidenceChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_KgEdges_FromNodeId",
                table: "KgEdges",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_KgEdges_KnowledgeDocumentId",
                table: "KgEdges",
                column: "KnowledgeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KgEdges_KnowledgeSourceId",
                table: "KgEdges",
                column: "KnowledgeSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_KgEdges_ToNodeId",
                table: "KgEdges",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_KgNodes_NormalizedName",
                table: "KgNodes",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_KgNodes_NormalizedName_Type",
                table: "KgNodes",
                columns: new[] { "NormalizedName", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KgAliases");

            migrationBuilder.DropTable(
                name: "KgEdges");

            migrationBuilder.DropTable(
                name: "KgNodes");
        }
    }
}
