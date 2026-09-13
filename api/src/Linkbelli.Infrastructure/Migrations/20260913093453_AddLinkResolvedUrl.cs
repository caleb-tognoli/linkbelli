using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkResolvedUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolvedUrl",
                table: "Links",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedUrlHash",
                table: "Links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_ResolvedUrlHash",
                table: "Links",
                column: "ResolvedUrlHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Links_ResolvedUrlHash",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "ResolvedUrl",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "ResolvedUrlHash",
                table: "Links");
        }
    }
}
