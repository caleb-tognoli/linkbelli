using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AutomationAppliedAt",
                table: "PlaylistItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: true),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TitlePattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UrlPattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: true),
                    AddTags = table.Column<string[]>(type: "text[]", nullable: false),
                    MoveToPlaylistId = table.Column<Guid>(type: "uuid", nullable: true),
                    CopyToPlaylistId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkWatched = table.Column<bool>(type: "boolean", nullable: false),
                    Trash = table.Column<bool>(type: "boolean", nullable: false),
                    StopOnMatch = table.Column<bool>(type: "boolean", nullable: false),
                    MatchCount = table.Column<int>(type: "integer", nullable: false),
                    LastMatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_AwaitingAutomation",
                table: "PlaylistItems",
                column: "CreationTime",
                filter: "\"AutomationAppliedAt\" IS NULL");

            // Everything already saved counts as already seen. A rule says "when this arrives",
            // not "reorganise everything I have ever saved" -- and without this, the first rule
            // anyone writes would be applied retroactively to their whole collection.
            migrationBuilder.Sql("""
                UPDATE "PlaylistItems" SET "AutomationAppliedAt" = now()
                WHERE "AutomationAppliedAt" IS NULL
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_OwnerId_Position",
                table: "AutomationRules",
                columns: new[] { "OwnerId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_AwaitingAutomation",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "AutomationAppliedAt",
                table: "PlaylistItems");
        }
    }
}
