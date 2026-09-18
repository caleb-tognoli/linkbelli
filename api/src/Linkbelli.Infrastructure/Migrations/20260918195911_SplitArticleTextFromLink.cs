using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linkbelli.Infrastructure.Migrations
{
    /// <summary>
    /// Empty on purpose: the article text moved off the Link entity, not off the row.
    /// </summary>
    /// <remarks>
    /// LinkContent is mapped onto the same table and the same column (table splitting), so the
    /// database is untouched and there is no data to move. This exists so the model snapshot
    /// records the change — without it the next migration would carry it silently, and a check
    /// for pending model changes would fail.
    /// </remarks>
    public partial class SplitArticleTextFromLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
