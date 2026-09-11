using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaylistPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sort = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ShowUrls = table.Column<bool>(type: "boolean", nullable: false),
                    ShowThumbnails = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistPreferences_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistPreferences_OwnerId_PlaylistId",
                table: "PlaylistPreferences",
                columns: new[] { "OwnerId", "PlaylistId" },
                unique: true,
                filter: "\"DeletionTime\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistPreferences_PlaylistId",
                table: "PlaylistPreferences",
                column: "PlaylistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistPreferences");
        }
    }
}
