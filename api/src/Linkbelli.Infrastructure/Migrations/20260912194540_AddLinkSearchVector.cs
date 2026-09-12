using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Links",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(\"Title\", '')), 'A') || setweight(to_tsvector('english', coalesce(\"SiteName\", '')), 'B') || setweight(to_tsvector('english', coalesce(\"Description\", '')), 'C') || setweight(to_tsvector('english', coalesce(\"Content\", '')), 'D')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_SearchVector",
                table: "Links",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Links_SearchVector",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Links");
        }
    }
}
