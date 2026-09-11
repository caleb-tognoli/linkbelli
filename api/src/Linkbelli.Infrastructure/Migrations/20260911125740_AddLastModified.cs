using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastModified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "UserQuotas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Tags",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "SourceTemplates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "SourceRuns",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "SavedSearches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "PlaylistTags",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "PlaylistSources",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Playlists",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "PlaylistPreferences",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "PlaylistItemTags",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "PlaylistItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Links",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Hosts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Folders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "FolderPlaylists",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "ApiKeys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing rows have no change history. Their last known change is the one that
            // deleted them, or failing that their creation — which is the honest answer and
            // keeps a first sync from claiming everything changed just now.
            migrationBuilder.Sql("""UPDATE "ApiKeys" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "FolderPlaylists" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "Folders" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "Hosts" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "Links" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "PlaylistItemTags" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "PlaylistItems" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "PlaylistPreferences" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "PlaylistSources" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "PlaylistTags" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "Playlists" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "SavedSearches" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "SourceRuns" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "SourceTemplates" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "Sources" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "Tags" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
            migrationBuilder.Sql("""UPDATE "UserQuotas" SET "LastModified" = COALESCE("DeletionTime", "CreationTime");""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "UserQuotas");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "SourceTemplates");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "SourceRuns");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "PlaylistTags");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "PlaylistSources");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "PlaylistPreferences");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "PlaylistItemTags");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "FolderPlaylists");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "ApiKeys");
        }
    }
}
