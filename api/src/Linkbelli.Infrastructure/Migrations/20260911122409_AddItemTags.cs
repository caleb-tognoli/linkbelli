using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaylistItemTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaylistItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistItemTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistItemTags_PlaylistItems_PlaylistItemId",
                        column: x => x.PlaylistItemId,
                        principalTable: "PlaylistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistItemTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItemTags_PlaylistItemId_TagId",
                table: "PlaylistItemTags",
                columns: new[] { "PlaylistItemId", "TagId" },
                unique: true,
                filter: "\"DeletionTime\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItemTags_TagId",
                table: "PlaylistItemTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistItemTags");
        }
    }
}
