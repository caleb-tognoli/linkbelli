using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DigestSentAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnFollow",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // True, including for accounts that already exist: a playlist shared with you is invisible
            // until you happen to look in the right place, so without this it may as well not have
            // been shared. Rare, and always caused by somebody acting on your account.
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnShare",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // True for existing accounts as well. A source that has given up means a playlist has
            // quietly stopped filling, and nothing else in the app says so.
            migrationBuilder.AddColumn<bool>(
                name: "NotifySourceStopped",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // False, for new and existing accounts alike. A recurring email nobody asked for is
            // the surest way to make somebody resent an app, so the digest is opted into. Same
            // for NotifyOnFollow above: somebody else's activity, and it repeats.
            migrationBuilder.AddColumn<bool>(
                name: "NotifyWeeklyDigest",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigestSentAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyOnFollow",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyOnShare",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifySourceStopped",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyWeeklyDigest",
                table: "AspNetUsers");
        }
    }
}
