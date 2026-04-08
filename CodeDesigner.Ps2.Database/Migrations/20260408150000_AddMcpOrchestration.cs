using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpOrchestrations",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "INTEGER", nullable: false)
                                       .Annotation("Sqlite:Autoincrement", true),
                    Name        = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Strategy    = table.Column<string>(type: "TEXT", nullable: true),
                    Roles       = table.Column<string>(type: "TEXT", nullable: true),
                    Workflow    = table.Column<string>(type: "TEXT", nullable: true),
                    Rules       = table.Column<string>(type: "TEXT", nullable: true),
                    Notes       = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt   = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt   = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpOrchestrations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "McpOrchestrations");
        }
    }
}
