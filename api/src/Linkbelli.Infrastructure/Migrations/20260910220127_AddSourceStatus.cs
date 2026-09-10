using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Before this column existed, the web client expressed "disabled" by writing a cron
            // for February 30th — a date that never occurs. Those sources are genuinely paused,
            // so carry them over as Paused. Their real cadence was destroyed when the sentinel
            // was written, so they get the form's own default (hourly) to resume onto.
            migrationBuilder.Sql("""
                UPDATE "Sources"
                SET "Status" = 1, "Schedule" = '0 * * * *'
                WHERE "Schedule" = '0 0 30 2 *';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Put paused sources back on the sentinel cron the client used to recognise.
            migrationBuilder.Sql("""
                UPDATE "Sources"
                SET "Schedule" = '0 0 30 2 *'
                WHERE "Status" = 1;
                """);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Sources");
        }
    }
}
