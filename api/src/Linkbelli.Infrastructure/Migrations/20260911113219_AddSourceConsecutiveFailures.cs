using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceConsecutiveFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "Sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "Sources");
        }
    }
}
