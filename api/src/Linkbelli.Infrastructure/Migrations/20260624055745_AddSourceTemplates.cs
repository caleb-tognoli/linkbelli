using System;
using System.Collections.Generic;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Config",
                table: "Sources",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "Sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SourceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    BaseConfig = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    UserFields = table.Column<List<TemplateField>>(type: "jsonb", nullable: false),
                    DefaultSchedule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sources_TemplateId",
                table: "Sources",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceTemplates_OwnerId",
                table: "SourceTemplates",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_SourceTemplates_TemplateId",
                table: "Sources",
                column: "TemplateId",
                principalTable: "SourceTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sources_SourceTemplates_TemplateId",
                table: "Sources");

            migrationBuilder.DropTable(
                name: "SourceTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Sources_TemplateId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Sources");

            migrationBuilder.AlterColumn<string>(
                name: "Config",
                table: "Sources",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
