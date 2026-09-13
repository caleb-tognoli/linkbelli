using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenAutomationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArchiveRequested",
                table: "Links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Archive",
                table: "AutomationRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Broken",
                table: "AutomationRules",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxMinutes",
                table: "AutomationRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinMinutes",
                table: "AutomationRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetScore",
                table: "AutomationRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "AutomationRules",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveRequested",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "Archive",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "Broken",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MaxMinutes",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MinMinutes",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "SetScore",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "AutomationRules");
        }
    }
}
