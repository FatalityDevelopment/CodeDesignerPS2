using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabelSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SetName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IsAgentLead = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Rules = table.Column<string>(type: "TEXT", nullable: true),
                    Guidelines = table.Column<string>(type: "TEXT", nullable: true),
                    RecommendedWorkflow = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpMemories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpMemories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Labels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LabelSetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    Hidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ArgRegsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ReturnRegsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ElementStride = table.Column<int>(type: "INTEGER", nullable: true),
                    ElementCount = table.Column<int>(type: "INTEGER", nullable: true),
                    NodeNextOffset = table.Column<int>(type: "INTEGER", nullable: true),
                    NodeSize = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Labels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Labels_LabelSets_LabelSetId",
                        column: x => x.LabelSetId,
                        principalTable: "LabelSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpAgentTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    McpAgentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Phase = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgentTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpAgentTasks_McpAgents_McpAgentId",
                        column: x => x.McpAgentId,
                        principalTable: "McpAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabelMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LabelId = table.Column<int>(type: "INTEGER", nullable: false),
                    Offset = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelMembers_Labels_LabelId",
                        column: x => x.LabelId,
                        principalTable: "Labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelMembers_LabelId",
                table: "LabelMembers",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_Labels_LabelSetId_Address",
                table: "Labels",
                columns: new[] { "LabelSetId", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_LabelSets_GameId_SetName",
                table: "LabelSets",
                columns: new[] { "GameId", "SetName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentTasks_McpAgentId_Phase_SortOrder",
                table: "McpAgentTasks",
                columns: new[] { "McpAgentId", "Phase", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_McpConfigs_Key",
                table: "McpConfigs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpMemories_Name",
                table: "McpMemories",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabelMembers");

            migrationBuilder.DropTable(
                name: "McpAgentTasks");

            migrationBuilder.DropTable(
                name: "McpConfigs");

            migrationBuilder.DropTable(
                name: "McpMemories");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Labels");

            migrationBuilder.DropTable(
                name: "McpAgents");

            migrationBuilder.DropTable(
                name: "LabelSets");
        }
    }
}
