using System;
using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <summary>
    /// Adds the <c>AiActivityLogs</c> table: the Bridge persists every AI activity
    /// event it relays (tool calls, navigation, label updates, packet captures,
    /// session starts) so consumers such as the Discord bot can poll it.
    ///
    /// Like <c>AddChatSessions</c>, the <c>[DbContext]</c> + <c>[Migration]</c>
    /// attributes live here directly (no Designer partial) and the snapshot in
    /// <c>CodeDesignerDbContextModelSnapshot</c> is updated by hand.
    /// </summary>
    [DbContext(typeof(CodeDesignerDbContext))]
    [Migration("20261007120000_AddAiActivityLog")]
    public partial class AddAiActivityLog : Migration
    {
        /// <summary>
        /// Intentionally empty — see the XML doc on <c>AddChatSessions.BuildTargetModel</c>.
        /// </summary>
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiActivityLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ArgsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ok = table.Column<bool>(type: "bit", nullable: true),
                    ElapsedMs = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<long>(type: "bigint", nullable: true),
                    Region = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiActivityLogs_CreatedAt",
                table: "AiActivityLogs",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiActivityLogs");
        }
    }
}
