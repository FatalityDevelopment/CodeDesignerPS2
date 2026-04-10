using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelMembersView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create a view over child Label rows so that label struct members are
            // queryable via "SELECT * FROM LabelMembers" without duplicating data.
            // Members are stored as child Labels (ParentId IS NOT NULL) using the
            // self-referencing relationship introduced in AddRecursiveLabels.
            migrationBuilder.Sql(@"
                CREATE VIEW dbo.LabelMembers AS
                SELECT
                    l.Id,
                    l.ParentId   AS LabelId,
                    ISNULL(l.Offset, 0) AS Offset,
                    l.Name,
                    l.ValueType,
                    l.Dynamic,
                    l.Hidden
                FROM dbo.Labels l
                WHERE l.ParentId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.LabelMembers;");
        }
    }
}
