using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Identity never recorded this, so existing accounts have nothing to carry over.
            // Their earliest playlist is the closest honest answer to when they arrived; an
            // account with no playlists falls back to now rather than to year 1.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" u
                SET "CreatedAt" = COALESCE(
                    (SELECT MIN(p."CreationTime") FROM "Playlists" p WHERE p."OwnerId" = u."Id"),
                    NOW());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AspNetUsers");
        }
    }
}
