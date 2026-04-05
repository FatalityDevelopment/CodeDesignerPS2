using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSnapshotCheat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CoverArt = table.Column<byte[]>(type: "BLOB", nullable: true),
                    IsoPath = table.Column<string>(type: "TEXT", nullable: true),
                    CRC = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheatLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheatLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheatLists_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SnapshotDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SnapshotPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Snapshots_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Cheats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CheatListId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    RawCodes = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cheats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cheats_CheatLists_CheatListId",
                        column: x => x.CheatListId,
                        principalTable: "CheatLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheatLists_GameId",
                table: "CheatLists",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Cheats_CheatListId_SortOrder",
                table: "Cheats",
                columns: new[] { "CheatListId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_CRC",
                table: "Games",
                column: "CRC");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_GameId",
                table: "Snapshots",
                column: "GameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cheats");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "CheatLists");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
