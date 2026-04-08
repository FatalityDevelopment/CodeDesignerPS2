using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpAgentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "McpAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Objective",
                table: "McpAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionBegin",
                table: "McpAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrentTasks",
                table: "McpAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionEnd",
                table: "McpAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Research",
                table: "McpAgents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Status",          table: "McpAgents");
            migrationBuilder.DropColumn(name: "Objective",       table: "McpAgents");
            migrationBuilder.DropColumn(name: "SessionBegin",    table: "McpAgents");
            migrationBuilder.DropColumn(name: "ConcurrentTasks", table: "McpAgents");
            migrationBuilder.DropColumn(name: "SessionEnd",      table: "McpAgents");
            migrationBuilder.DropColumn(name: "Research",        table: "McpAgents");
        }
    }
}
