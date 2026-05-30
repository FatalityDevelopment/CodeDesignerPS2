using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCoordination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpAgentSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    McpAgentId = table.Column<int>(type: "int", nullable: true),
                    ParentSessionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrentTask = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgentSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpAgentSessions_McpAgentSessions_ParentSessionId",
                        column: x => x.ParentSessionId,
                        principalTable: "McpAgentSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_McpAgentSessions_McpAgents_McpAgentId",
                        column: x => x.McpAgentId,
                        principalTable: "McpAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "McpAgentMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromSessionId = table.Column<int>(type: "int", nullable: false),
                    ToSessionId = table.Column<int>(type: "int", nullable: true),
                    ToAgentId = table.Column<int>(type: "int", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgentMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpAgentMessages_McpAgentSessions_FromSessionId",
                        column: x => x.FromSessionId,
                        principalTable: "McpAgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpAgentMessages_McpAgentSessions_ToSessionId",
                        column: x => x.ToSessionId,
                        principalTable: "McpAgentSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_McpAgentMessages_McpAgents_ToAgentId",
                        column: x => x.ToAgentId,
                        principalTable: "McpAgents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentMessages_FromSessionId",
                table: "McpAgentMessages",
                column: "FromSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentMessages_ToAgentId_ReadAt",
                table: "McpAgentMessages",
                columns: new[] { "ToAgentId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentMessages_ToSessionId_ReadAt",
                table: "McpAgentMessages",
                columns: new[] { "ToSessionId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentSessions_McpAgentId",
                table: "McpAgentSessions",
                column: "McpAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentSessions_ParentSessionId",
                table: "McpAgentSessions",
                column: "ParentSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentSessions_Status",
                table: "McpAgentSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpAgentMessages");

            migrationBuilder.DropTable(
                name: "McpAgentSessions");
        }
    }
}
