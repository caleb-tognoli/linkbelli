using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkHostPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HostPath",
                table: "Links",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                computedColumnSql: "rtrim(regexp_replace(regexp_replace(\"CanonicalUrl\", '^https?://', ''), '\\?.*$', ''), '/')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_HostPath",
                table: "Links",
                column: "HostPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Links_HostPath",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "HostPath",
                table: "Links");
        }
    }
}
