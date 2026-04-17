using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpOrchestrationIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "McpOrchestrations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsActive", table: "McpOrchestrations");
        }
    }
}
