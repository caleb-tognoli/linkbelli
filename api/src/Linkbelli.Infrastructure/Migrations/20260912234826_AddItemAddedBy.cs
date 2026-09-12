using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemAddedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AddedByUserId",
                table: "PlaylistItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId_AddedByUserId",
                table: "PlaylistItems",
                columns: new[] { "PlaylistId", "AddedByUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_PlaylistId_AddedByUserId",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                table: "PlaylistItems");
        }
    }
}
