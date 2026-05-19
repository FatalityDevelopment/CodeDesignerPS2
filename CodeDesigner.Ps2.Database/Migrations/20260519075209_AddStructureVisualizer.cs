using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStructureVisualizer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallEdges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameDiscId = table.Column<int>(type: "int", nullable: true),
                    CallerAddress = table.Column<long>(type: "bigint", nullable: false),
                    CalleeAddress = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SourceLabelSetId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallEdges_GameDiscs_GameDiscId",
                        column: x => x.GameDiscId,
                        principalTable: "GameDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabelGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LabelSetId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelGroups_LabelSets_LabelSetId",
                        column: x => x.LabelSetId,
                        principalTable: "LabelSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabelGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LabelGroupId = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelGroupMembers_LabelGroups_LabelGroupId",
                        column: x => x.LabelGroupId,
                        principalTable: "LabelGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LabelGroupMembers_Labels_LabelId",
                        column: x => x.LabelId,
                        principalTable: "Labels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallEdges_GameDiscId_CalleeAddress",
                table: "CallEdges",
                columns: new[] { "GameDiscId", "CalleeAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_CallEdges_GameDiscId_CallerAddress",
                table: "CallEdges",
                columns: new[] { "GameDiscId", "CallerAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_LabelGroupMembers_LabelGroupId_LabelId",
                table: "LabelGroupMembers",
                columns: new[] { "LabelGroupId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelGroupMembers_LabelId",
                table: "LabelGroupMembers",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_LabelGroups_LabelSetId_Name",
                table: "LabelGroups",
                columns: new[] { "LabelSetId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallEdges");

            migrationBuilder.DropTable(
                name: "LabelGroupMembers");

            migrationBuilder.DropTable(
                name: "LabelGroups");
        }
    }
}
