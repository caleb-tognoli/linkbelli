using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserQuotas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxSources = table.Column<int>(type: "integer", nullable: false),
                    MaxRunsPerDay = table.Column<int>(type: "integer", nullable: false),
                    MaxItemsPerRun = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xmin", rowVersion: true, nullable: false),
                    CreationTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletionTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserQuotas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserQuotas_UserId",
                table: "UserQuotas",
                column: "UserId",
                unique: true,
                filter: "\"DeletionTime\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserQuotas");
        }
    }
}
