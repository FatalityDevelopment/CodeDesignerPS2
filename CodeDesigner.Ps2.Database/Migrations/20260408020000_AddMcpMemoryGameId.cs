using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpMemoryGameId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GameId",
                table: "McpMemories",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameId",
                table: "McpMemories");
        }
    }
}
