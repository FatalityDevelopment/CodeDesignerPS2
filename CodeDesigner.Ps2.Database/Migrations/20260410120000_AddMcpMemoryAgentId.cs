using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpMemoryAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentId",
                table: "McpMemories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpMemories_AgentId",
                table: "McpMemories",
                column: "AgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_McpMemories_McpAgents_AgentId",
                table: "McpMemories",
                column: "AgentId",
                principalTable: "McpAgents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_McpMemories_McpAgents_AgentId",
                table: "McpMemories");

            migrationBuilder.DropIndex(
                name: "IX_McpMemories_AgentId",
                table: "McpMemories");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "McpMemories");
        }
    }
}
