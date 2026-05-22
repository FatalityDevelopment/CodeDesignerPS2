using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <summary>
    /// Adds the <c>IsSystem</c> flag to <c>LabelSets</c> so CodeDesigner-authored sets
    /// (built-in EE labels, runtime IOP module exports) can be distinguished from user
    /// sets in the UI.
    /// </summary>
    public partial class AddLabelSetIsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "LabelSets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Existing built-in rows should also be treated as system sets so they pick up
            // the new amber styling alongside any future IOP per-module sets.
            migrationBuilder.Sql("UPDATE LabelSets SET IsSystem = 1 WHERE IsBuiltIn = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "LabelSets");
        }
    }
}
