using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddXminConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Sources",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "SourceRuns",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PlaylistSources",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Playlists",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PlaylistItems",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Links",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Hosts",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ApiKeys",
                type: "xmin",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "SourceRuns");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PlaylistSources");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ApiKeys");
        }
    }
}
