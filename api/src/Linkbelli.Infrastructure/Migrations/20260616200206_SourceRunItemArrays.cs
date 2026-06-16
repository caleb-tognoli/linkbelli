using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceRunItemArrays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Counts cannot be converted into the URLs they once represented, so the columns
            // are dropped and recreated as empty arrays rather than cast in place.
            migrationBuilder.DropColumn(name: "ItemsFound", table: "SourceRuns");
            migrationBuilder.DropColumn(name: "ItemsAdded", table: "SourceRuns");

            migrationBuilder.AddColumn<string[]>(
                name: "ItemsFound",
                table: "SourceRuns",
                type: "text[]",
                nullable: false,
                defaultValue: Array.Empty<string>());

            migrationBuilder.AddColumn<string[]>(
                name: "ItemsAdded",
                table: "SourceRuns",
                type: "text[]",
                nullable: false,
                defaultValue: Array.Empty<string>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ItemsFound", table: "SourceRuns");
            migrationBuilder.DropColumn(name: "ItemsAdded", table: "SourceRuns");

            migrationBuilder.AddColumn<int>(
                name: "ItemsFound",
                table: "SourceRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ItemsAdded",
                table: "SourceRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
