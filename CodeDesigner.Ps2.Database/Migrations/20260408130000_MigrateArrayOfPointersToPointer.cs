using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class MigrateArrayOfPointersToPointer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert legacy ArrayOfPointers labels (Type = 4) to Address (Type = 0)
            // with ValueType = 'pointer'. This aligns with the unified type system where
            // 'pointer' is a data-type annotation rather than a separate semantic type.
            migrationBuilder.Sql(@"
                UPDATE Labels
                SET    Type      = 0,
                       ValueType = COALESCE(ValueType, 'pointer')
                WHERE  Type = 4;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: restore rows that have ValueType = 'pointer' and Type = 0
            // back to ArrayOfPointers (Type = 4). Note: this is a best-effort
            // reversal — rows that already had a non-null ValueType before the Up
            // migration cannot be distinguished here.
            migrationBuilder.Sql(@"
                UPDATE Labels
                SET    Type      = 4,
                       ValueType = NULL
                WHERE  Type = 0 AND ValueType = 'pointer';
            ");
        }
    }
}
