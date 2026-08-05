using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateVisibilityTagsAndSaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "SourceTemplates");

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "SourceTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TemplateTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateTags_SourceTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "SourceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TemplateTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSavedTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSavedTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSavedTemplates_SourceTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "SourceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateTags_TagId",
                table: "TemplateTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateTags_TemplateId_TagId",
                table: "TemplateTags",
                columns: new[] { "TemplateId", "TagId" },
                unique: true,
                filter: "\"DeletionTime\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSavedTemplates_TemplateId",
                table: "UserSavedTemplates",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSavedTemplates_UserId_TemplateId",
                table: "UserSavedTemplates",
                columns: new[] { "UserId", "TemplateId" },
                unique: true,
                filter: "\"DeletionTime\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateTags");

            migrationBuilder.DropTable(
                name: "UserSavedTemplates");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "SourceTemplates");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "SourceTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
