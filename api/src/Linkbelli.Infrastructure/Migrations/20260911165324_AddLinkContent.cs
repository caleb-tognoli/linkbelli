using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "Links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContentTruncated",
                table: "Links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WordCount",
                table: "Links",
                type: "integer",
                nullable: true);

            // Searching the article text uses the same lower(col) LIKE idiom as every other
            // field, so it needs the same kind of index to stay off a sequential scan. Stored
            // text is capped at ArticleExtractor.MaxLength, which is what bounds this index.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Links_Content_trgm"
                ON "Links" USING GIN (lower("Content") gin_trgm_ops)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Links_Content_trgm"
                """);

            migrationBuilder.DropColumn(
                name: "Content",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "ContentTruncated",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "WordCount",
                table: "Links");
        }
    }
}
