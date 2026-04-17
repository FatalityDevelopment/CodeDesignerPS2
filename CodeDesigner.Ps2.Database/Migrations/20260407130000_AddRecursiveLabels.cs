using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRecursiveLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add new columns to Labels.
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Labels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Offset",
                table: "Labels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                table: "Labels",
                type: "TEXT",
                nullable: true);

            // 2. Migrate existing LabelMember rows to child Label rows.
            //    The synthetic address for each child is: parentAddress + ":+" + upper-hex(offset, 4 digits).
            migrationBuilder.Sql(@"
                INSERT INTO Labels (LabelSetId, ParentId, Offset, Address, Name, Comment, ValueType, Hidden, Dynamic, Type,
                                    ArgRegsJson, ReturnRegsJson, ElementStride, ElementCount, NodeNextOffset, NodeSize)
                SELECT
                    l.LabelSetId,
                    lm.LabelId   AS ParentId,
                    lm.Offset    AS Offset,
                    l.Address || ':+0x' || upper(printf('%04X', lm.Offset)) AS Address,
                    lm.Name,
                    NULL         AS Comment,
                    NULL         AS ValueType,
                    lm.Hidden,
                    lm.Dynamic,
                    0            AS Type,
                    NULL, NULL, NULL, NULL, NULL, NULL
                FROM LabelMembers lm
                JOIN Labels l ON l.Id = lm.LabelId;
            ");

            // 3. Create the self-referencing FK index.
            migrationBuilder.CreateIndex(
                name: "IX_Labels_ParentId",
                table: "Labels",
                column: "ParentId");

            // 4. Add the FK constraint (SQLite enforces only when FK pragma is on;
            //    still declared for EF Core model integrity and future DB engines).
            migrationBuilder.AddForeignKey(
                name: "FK_Labels_Labels_ParentId",
                table: "Labels",
                column: "ParentId",
                principalTable: "Labels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 5. Drop the now-superseded LabelMembers table.
            migrationBuilder.DropTable(name: "LabelMembers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-create the LabelMembers table.
            migrationBuilder.CreateTable(
                name: "LabelMembers",
                columns: table => new
                {
                    Id      = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
                    LabelId = table.Column<int>(nullable: false),
                    Offset  = table.Column<int>(nullable: false),
                    Name    = table.Column<string>(maxLength: 256, nullable: false),
                    Dynamic = table.Column<bool>(nullable: false),
                    Hidden  = table.Column<bool>(nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelMembers_Labels_LabelId",
                        column: x => x.LabelId,
                        principalTable: "Labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.DropForeignKey(name: "FK_Labels_Labels_ParentId", table: "Labels");
            migrationBuilder.DropIndex(name: "IX_Labels_ParentId", table: "Labels");
            migrationBuilder.DropColumn(name: "ParentId",  table: "Labels");
            migrationBuilder.DropColumn(name: "Offset",    table: "Labels");
            migrationBuilder.DropColumn(name: "ValueType", table: "Labels");
        }
    }
}
