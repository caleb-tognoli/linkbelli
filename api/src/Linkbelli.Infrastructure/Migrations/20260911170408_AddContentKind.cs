using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "SavedSearches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxMinutes",
                table: "SavedSearches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Feeds the classification sweep, which reads the oldest unclassified links first.
            // Partial, so it shrinks to nothing as the backfill catches up rather than becoming
            // a permanent index over a column with eight distinct values.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Links_Unclassified"
                ON "Links" ("CreationTime")
                WHERE "Kind" = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Links_Unclassified"
                """);


            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "MaxMinutes",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Links");
        }
    }
}
