using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnrichmentError",
                table: "Links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnrichmentStatus",
                table: "Links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailureCount",
                table: "Links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCheckedAt",
                table: "Links",
                type: "timestamp with time zone",
                nullable: true);

            // Before this, a permanent failure was stamped as enriched with the reason hidden
            // inside the OpenGraph metadata bag. Recover those: anything carrying an
            // "enrichmentError" key was a failure, and the key itself polluted the raw OG data.
            migrationBuilder.Sql("""
                UPDATE "Links"
                SET "EnrichmentStatus" = 2,
                    "EnrichmentError" = "Metadata"::jsonb ->> 'enrichmentError',
                    "FailureCount" = 1,
                    "LastCheckedAt" = "EnrichedAt",
                    "Metadata" = NULL
                WHERE "Metadata" IS NOT NULL
                  AND "Metadata"::jsonb ? 'enrichmentError';
                """);

            // Everything else that finished is a success; anything unfinished stays Pending.
            migrationBuilder.Sql("""
                UPDATE "Links"
                SET "EnrichmentStatus" = 1,
                    "LastCheckedAt" = "EnrichedAt"
                WHERE "EnrichedAt" IS NOT NULL
                  AND "EnrichmentStatus" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnrichmentError",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "EnrichmentStatus",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "FailureCount",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "Links");
        }
    }
}
