using System;
using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <summary>
    /// Adds the persistent chat tables backing the AI control panel's Chat tab.
    /// One <c>McpChatSessions</c> row per visible thread; <c>McpChatMessages</c>
    /// owns the rendered bubbles (block payload stored as JSON to mirror the
    /// wire format documented in <c>docs/ai-chat-templates.md</c>).
    ///
    /// The <c>[DbContext]</c> + <c>[Migration]</c> attributes that normally live
    /// on the auto-generated Designer partial are placed here directly so this
    /// migration can be discovered by <c>Database.Migrate()</c> without the
    /// dev-tooling round-trip through <c>dotnet ef migrations add</c>. The
    /// snapshot in <c>CodeDesignerDbContextModelSnapshot</c> is updated by hand
    /// alongside this file.
    /// </summary>
    [DbContext(typeof(CodeDesignerDbContext))]
    [Migration("20260604180000_AddChatSessions")]
    public partial class AddChatSessions : Migration
    {
        /// <summary>
        /// Snapshot of the model AFTER this migration is applied. <c>Database.Migrate()</c>
        /// does not consult this method — only EF design-time tooling
        /// (<c>dotnet ef migrations add/remove</c>) does. We leave it empty
        /// here; the canonical post-state lives in
        /// <see cref="CodeDesignerDbContextModelSnapshot"/>.
        /// </summary>
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // Intentionally empty — see XML doc above.
        }


        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    McpOrchestrationId = table.Column<int>(type: "int", nullable: true),
                    GameSerial = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    GameTag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpChatSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpChatSessions_McpOrchestrations_McpOrchestrationId",
                        column: x => x.McpOrchestrationId,
                        principalTable: "McpOrchestrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "McpChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    McpChatSessionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    McpAgentId = table.Column<int>(type: "int", nullable: true),
                    AgentDisplay = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BlocksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tokens = table.Column<int>(type: "int", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpChatMessages_McpChatSessions_McpChatSessionId",
                        column: x => x.McpChatSessionId,
                        principalTable: "McpChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpChatMessages_McpAgents_McpAgentId",
                        column: x => x.McpAgentId,
                        principalTable: "McpAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpChatSessions_McpOrchestrationId",
                table: "McpChatSessions",
                column: "McpOrchestrationId");

            migrationBuilder.CreateIndex(
                name: "IX_McpChatSessions_UpdatedAt",
                table: "McpChatSessions",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_McpChatMessages_McpAgentId",
                table: "McpChatMessages",
                column: "McpAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_McpChatMessages_McpChatSessionId_CreatedAt",
                table: "McpChatMessages",
                columns: new[] { "McpChatSessionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpChatMessages");

            migrationBuilder.DropTable(
                name: "McpChatSessions");
        }
    }
}
