using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Filter",
                table: "Sources",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkippedCount",
                table: "SourceRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Filter",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "SkippedCount",
                table: "SourceRuns");
        }
    }
}
