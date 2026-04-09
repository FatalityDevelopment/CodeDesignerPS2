using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelSetGameDiscId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the new nullable FK column.
            migrationBuilder.AddColumn<int>(
                name: "GameDiscId",
                table: "LabelSets",
                type: "INTEGER",
                nullable: true);

            // 2. Migrate existing rows: legacy GameId stores a Games.Id integer string.
            //    Find the first GameDisc for that game and populate GameDiscId.
            migrationBuilder.Sql(@"
                UPDATE LabelSets
                SET GameDiscId = (
                    SELECT gd.Id
                    FROM GameDiscs gd
                    WHERE gd.GameId = CAST(LabelSets.GameId AS INTEGER)
                    LIMIT 1
                )
                WHERE LabelSets.GameId IS NOT NULL
                  AND LabelSets.GameId GLOB '[0-9]*';
            ");

            // 3. Drop the old unique index on (GameId, SetName).
            migrationBuilder.DropIndex(
                name: "IX_LabelSets_GameId_SetName",
                table: "LabelSets");

            // 4. Add FK constraint + new unique index on (GameDiscId, SetName).
            migrationBuilder.CreateIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets",
                columns: new[] { "GameDiscId", "SetName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LabelSets_GameDiscs_GameDiscId",
                table: "LabelSets",
                column: "GameDiscId",
                principalTable: "GameDiscs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabelSets_GameDiscs_GameDiscId",
                table: "LabelSets");

            migrationBuilder.DropIndex(
                name: "IX_LabelSets_GameDiscId_SetName",
                table: "LabelSets");

            // Restore the old unique index on (GameId, SetName).
            migrationBuilder.CreateIndex(
                name: "IX_LabelSets_GameId_SetName",
                table: "LabelSets",
                columns: new[] { "GameId", "SetName" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "GameDiscId",
                table: "LabelSets");
        }
    }
}
