using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "PlaylistItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SharedAt",
                table: "PlaylistItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_ShareToken",
                table: "PlaylistItems",
                column: "ShareToken",
                unique: true,
                filter: "\"ShareToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_ShareToken",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "SharedAt",
                table: "PlaylistItems");
        }
    }
}
