using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <summary>
    /// Makes item search indexable. It matches with <c>lower(col) LIKE '%term%'</c>, which no
    /// B-tree index can serve — so every keystroke sequentially scanned a table that grows by up
    /// to a hundred rows per source per run. Trigram GIN indexes cover exactly that predicate.
    /// </summary>
    public partial class AddSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            // Expression indexes, matching the lower(...) the query applies. Partial on the live
            // rows only, mirroring the soft-delete filter every read already carries.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Links_Title_trgm"
                ON "Links" USING GIN (lower("Title") gin_trgm_ops)
                WHERE "DeletionTime" IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Links_Description_trgm"
                ON "Links" USING GIN (lower("Description") gin_trgm_ops)
                WHERE "DeletionTime" IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Links_SiteName_trgm"
                ON "Links" USING GIN (lower("SiteName") gin_trgm_ops)
                WHERE "DeletionTime" IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Links_CanonicalUrl_trgm"
                ON "Links" USING GIN (lower("CanonicalUrl") gin_trgm_ops)
                WHERE "DeletionTime" IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Hosts_Hostname_trgm"
                ON "Hosts" USING GIN (lower("Hostname") gin_trgm_ops)
                WHERE "DeletionTime" IS NULL;
                """);

            // Playlist and tag names are searched the same way from discovery.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Playlists_Name_trgm"
                ON "Playlists" USING GIN (lower("Name") gin_trgm_ops)
                WHERE "DeletionTime" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Playlists_Name_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Hosts_Hostname_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Links_CanonicalUrl_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Links_SiteName_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Links_Description_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Links_Title_trgm";""");

            // The extension is left in place: other things may have come to depend on it, and
            // dropping it would take every trigram index in the database with it.
        }
    }
}
