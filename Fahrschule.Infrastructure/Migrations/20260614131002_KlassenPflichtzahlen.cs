using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KlassenPflichtzahlen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredSpecialDrivesHighway",
                table: "LicenseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSpecialDrivesNight",
                table: "LicenseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSpecialDrivesOverland",
                table: "LicenseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredTheoryDoubleLessons",
                table: "LicenseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Seed the legal starting values for an existing class B (§ 5
            // FahrSchAusbO): 2 Zusatzstoff double lessons, special drives
            // Überland 5 / Autobahn 4 / Nacht 3. Only touches a still-default B
            // (all four at 0), so it never overwrites values the owner already
            // entered. Editable in the admin panel afterwards.
            migrationBuilder.Sql(
                "UPDATE \"LicenseClasses\" SET " +
                "\"RequiredTheoryDoubleLessons\" = 2, " +
                "\"RequiredSpecialDrivesOverland\" = 5, " +
                "\"RequiredSpecialDrivesHighway\" = 4, " +
                "\"RequiredSpecialDrivesNight\" = 3 " +
                "WHERE \"Code\" = 'B' AND \"RequiredTheoryDoubleLessons\" = 0 " +
                "AND \"RequiredSpecialDrivesOverland\" = 0 AND \"RequiredSpecialDrivesHighway\" = 0 " +
                "AND \"RequiredSpecialDrivesNight\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredSpecialDrivesHighway",
                table: "LicenseClasses");

            migrationBuilder.DropColumn(
                name: "RequiredSpecialDrivesNight",
                table: "LicenseClasses");

            migrationBuilder.DropColumn(
                name: "RequiredSpecialDrivesOverland",
                table: "LicenseClasses");

            migrationBuilder.DropColumn(
                name: "RequiredTheoryDoubleLessons",
                table: "LicenseClasses");
        }
    }
}
