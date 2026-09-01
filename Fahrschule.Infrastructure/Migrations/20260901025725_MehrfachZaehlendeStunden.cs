using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <summary>
    /// A lesson may now count a point SEVERAL times (two Autobahnfahrten driven in
    /// one go): the yes/no flag "CountsTowardRequirement" becomes the number
    /// "CountedSessions".
    ///
    /// The scaffolded version of this migration dropped the old column and added
    /// the new one - which would silently forget which coverages counted. So the
    /// order here is add → fill → drop, and the value is taken from the counted
    /// sessions that actually exist (StudentProgressEntry). Those rows are the
    /// truth the counters have always been computed from; the flag was only their
    /// mirror. Under the old model there is at most one per lesson and point, so
    /// every existing row ends up with exactly 0 or 1 - and any drift between
    /// flag and rows is quietly repaired on the way.
    /// </summary>
    public partial class MehrfachZaehlendeStunden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountedSessions",
                table: "LessonItem",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "LessonItem" li
                SET "CountedSessions" = (
                    SELECT COUNT(*)
                    FROM "StudentProgressEntry" e
                    WHERE e."LessonId" = li."LessonId"
                      AND e."StudentProgressItemId" = li."StudentProgressItemId");
                """);

            migrationBuilder.DropColumn(
                name: "CountsTowardRequirement",
                table: "LessonItem");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardRequirement",
                table: "LessonItem",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Back to the yes/no world: anything counted at least once counted.
            // (A lesson that counted twice cannot be expressed there any more -
            // the counted sessions themselves stay untouched either way.)
            migrationBuilder.Sql("""
                UPDATE "LessonItem" SET "CountsTowardRequirement" = ("CountedSessions" > 0);
                """);

            migrationBuilder.DropColumn(
                name: "CountedSessions",
                table: "LessonItem");
        }
    }
}
