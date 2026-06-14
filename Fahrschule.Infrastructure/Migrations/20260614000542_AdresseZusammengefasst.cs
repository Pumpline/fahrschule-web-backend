using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdresseZusammengefasst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new combined column FIRST, then merge the existing
            // Street/PLZ/Ort into it, and only then drop the old columns - so no
            // address data is lost. Empty parts collapse to NULL.
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Students",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Students\" SET \"Address\" = NULLIF(btrim(concat_ws(', ', " +
                "NULLIF(btrim(\"Street\"), ''), " +
                "NULLIF(btrim(concat_ws(' ', NULLIF(btrim(\"PostalCode\"), ''), NULLIF(btrim(\"City\"), ''))), ''))), '');");

            migrationBuilder.DropColumn(name: "City", table: "Students");
            migrationBuilder.DropColumn(name: "PostalCode", table: "Students");
            migrationBuilder.DropColumn(name: "Street", table: "Students");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Students");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Students",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Students",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
