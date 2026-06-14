using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditKategorien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AuditLogs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Backfill existing entries so the role-visibility filter works for
            // them too. Keep this CASE in sync with AuditCategory.For(...).
            migrationBuilder.Sql(
                "UPDATE \"AuditLogs\" SET \"Category\" = CASE " +
                "WHEN \"EntityType\" = 'Benutzer' AND \"Action\" IN ('PasswortGeändert', 'PasswortZurückgesetzt') THEN 'security' " +
                "WHEN \"EntityType\" = 'Benutzer' THEN 'users' " +
                "WHEN \"EntityType\" = 'Schüler' THEN 'students' " +
                "WHEN \"EntityType\" IN ('Ausbildungsfortschritt', 'Ausbildungsstunde', 'Prüfung', 'Unterlage-Schüler') THEN 'training' " +
                "WHEN \"EntityType\" = 'Termin' THEN 'calendar' " +
                "WHEN \"EntityType\" IN ('Führerscheinklasse', 'Unterlage', 'Ausbildungsplan-Punkt', 'Einstellungen') THEN 'setup' " +
                "ELSE 'security' END;");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Category",
                table: "AuditLogs",
                column: "Category");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Category",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AuditLogs");
        }
    }
}
