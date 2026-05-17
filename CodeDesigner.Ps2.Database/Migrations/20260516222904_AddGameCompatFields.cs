using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeDesigner.Ps2.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCompatFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompatStatus",
                table: "GameDiscs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForumLink",
                table: "GameDiscs",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTestedDate",
                table: "GameDiscs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestedVersion",
                table: "GameDiscs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikiLink",
                table: "GameDiscs",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompatStatus",
                table: "GameDiscs");

            migrationBuilder.DropColumn(
                name: "ForumLink",
                table: "GameDiscs");

            migrationBuilder.DropColumn(
                name: "LastTestedDate",
                table: "GameDiscs");

            migrationBuilder.DropColumn(
                name: "LastTestedVersion",
                table: "GameDiscs");

            migrationBuilder.DropColumn(
                name: "WikiLink",
                table: "GameDiscs");
        }
    }
}
