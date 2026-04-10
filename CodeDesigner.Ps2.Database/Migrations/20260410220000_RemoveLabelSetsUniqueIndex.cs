using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLabelSetsUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets");

            migrationBuilder.CreateIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets",
                columns: new[] { "GameDiscId", "SetName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets");

            migrationBuilder.CreateIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets",
                columns: new[] { "GameDiscId", "SetName" },
                unique: true,
                filter: "[GameDiscId] IS NOT NULL");
        }
    }
}
