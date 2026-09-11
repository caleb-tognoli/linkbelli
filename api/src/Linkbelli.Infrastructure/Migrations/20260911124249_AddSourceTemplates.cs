using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some development databases carry a half-built version of this feature: a
            // SourceTemplates / TemplateTags / UserSavedTemplates cluster and a Sources.TemplateId
            // column, created by hand, tracked by no migration, referenced by no code, and empty.
            // They only collide with the real thing. Dropped in dependency order; on a fresh
            // database every statement here is a no-op.
            migrationBuilder.Sql("""ALTER TABLE IF EXISTS "Sources" DROP COLUMN IF EXISTS "TemplateId";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "UserSavedTemplates";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "TemplateTags";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "SourceTemplates";""");

            migrationBuilder.CreateTable(
                name: "SourceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    BaseConfig = table.Column<string>(type: "jsonb", nullable: false),
                    Fields = table.Column<string>(type: "jsonb", nullable: false),
                    Builtin = table.Column<bool>(type: "boolean", nullable: false),
                    SuggestedSchedule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceTemplates_Key",
                table: "SourceTemplates",
                column: "Key",
                unique: true,
                filter: "\"DeletionTime\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceTemplates");
        }
    }
}
