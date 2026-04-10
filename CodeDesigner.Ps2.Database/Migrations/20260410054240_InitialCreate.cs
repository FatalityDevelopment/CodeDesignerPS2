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
                name: "GameRegions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRegions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CoverArt = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsAgentLead = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Guidelines = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecommendedWorkflow = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Objective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SessionBegin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrentTasks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SessionEnd = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Research = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpMemories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GameId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpMemories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpOrchestrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Strategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Roles = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Workflow = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpOrchestrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "GameDiscs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    GameRegionId = table.Column<int>(type: "int", nullable: false),
                    Serial = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CRC = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsoPath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameDiscs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameDiscs_GameRegions_GameRegionId",
                        column: x => x.GameRegionId,
                        principalTable: "GameRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameDiscs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SnapshotPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
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
                name: "McpAgentTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    McpAgentId = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                name: "CheatLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameId = table.Column<int>(type: "int", nullable: true),
                    GameDiscId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheatLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheatLists_GameDiscs_GameDiscId",
                        column: x => x.GameDiscId,
                        principalTable: "GameDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CheatLists_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "LabelSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    GameDiscId = table.Column<int>(type: "int", nullable: true),
                    GameId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelSets_GameDiscs_GameDiscId",
                        column: x => x.GameDiscId,
                        principalTable: "GameDiscs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cheats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheatListId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Author = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "Labels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LabelSetId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Offset = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValueType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Hidden = table.Column<bool>(type: "bit", nullable: false),
                    Dynamic = table.Column<bool>(type: "bit", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ArgRegsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnRegsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ElementStride = table.Column<int>(type: "int", nullable: true),
                    ElementCount = table.Column<int>(type: "int", nullable: true),
                    NodeNextOffset = table.Column<int>(type: "int", nullable: true),
                    NodeSize = table.Column<int>(type: "int", nullable: true)
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
                    table.ForeignKey(
                        name: "FK_Labels_Labels_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheatLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheatId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheatLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheatLines_Cheats_CheatId",
                        column: x => x.CheatId,
                        principalTable: "Cheats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheatLines_CheatId_SortOrder",
                table: "CheatLines",
                columns: new[] { "CheatId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheatLists_GameDiscId",
                table: "CheatLists",
                column: "GameDiscId");

            migrationBuilder.CreateIndex(
                name: "IX_CheatLists_GameId",
                table: "CheatLists",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Cheats_CheatListId_SortOrder",
                table: "Cheats",
                columns: new[] { "CheatListId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GameDiscs_CRC",
                table: "GameDiscs",
                column: "CRC");

            migrationBuilder.CreateIndex(
                name: "IX_GameDiscs_GameId_GameRegionId",
                table: "GameDiscs",
                columns: new[] { "GameId", "GameRegionId" });

            migrationBuilder.CreateIndex(
                name: "IX_GameDiscs_GameRegionId",
                table: "GameDiscs",
                column: "GameRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_GameDiscs_Serial",
                table: "GameDiscs",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_GameRegions_Name",
                table: "GameRegions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_Title",
                table: "Games",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Labels_LabelSetId_Address",
                table: "Labels",
                columns: new[] { "LabelSetId", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_Labels_ParentId",
                table: "Labels",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets",
                columns: new[] { "GameDiscId", "SetName" },
                unique: true,
                filter: "[GameDiscId] IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_GameId",
                table: "Snapshots",
                column: "GameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheatLines");

            migrationBuilder.DropTable(
                name: "Labels");

            migrationBuilder.DropTable(
                name: "McpAgentTasks");

            migrationBuilder.DropTable(
                name: "McpConfigs");

            migrationBuilder.DropTable(
                name: "McpMemories");

            migrationBuilder.DropTable(
                name: "McpOrchestrations");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "Cheats");

            migrationBuilder.DropTable(
                name: "LabelSets");

            migrationBuilder.DropTable(
                name: "McpAgents");

            migrationBuilder.DropTable(
                name: "CheatLists");

            migrationBuilder.DropTable(
                name: "GameDiscs");

            migrationBuilder.DropTable(
                name: "GameRegions");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
