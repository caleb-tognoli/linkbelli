using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceRunCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AddedCount",
                table: "SourceRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FoundCount",
                table: "SourceRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing runs carry the full URL list; the counts come from its length, and the
            // list itself is cut down to the sample the app now keeps. The URLs were duplicated
            // verbatim from Link.CanonicalUrl, so nothing is lost that isn't still on the link.
            migrationBuilder.Sql("""
                UPDATE "SourceRuns"
                SET "FoundCount" = COALESCE(array_length("ItemsFound", 1), 0),
                    "AddedCount" = COALESCE(array_length("ItemsAdded", 1), 0),
                    "ItemsFound" = "ItemsFound"[1:20],
                    "ItemsAdded" = "ItemsAdded"[1:20];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedCount",
                table: "SourceRuns");

            migrationBuilder.DropColumn(
                name: "FoundCount",
                table: "SourceRuns");
        }
    }
}
