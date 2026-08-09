using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Vorbesitz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriorLicenseNote",
                table: "Students",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredBasicTheoryLessonsOverride",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredBasicTheoryLessonsOverrideReason",
                table: "Students",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StudentPriorLicenseClass",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPriorLicenseClass", x => new { x.StudentId, x.LicenseClassId });
                    table.ForeignKey(
                        name: "FK_StudentPriorLicenseClass_LicenseClasses_LicenseClassId",
                        column: x => x.LicenseClassId,
                        principalTable: "LicenseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentPriorLicenseClass_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentPriorLicenseClass_LicenseClassId",
                table: "StudentPriorLicenseClass",
                column: "LicenseClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentPriorLicenseClass");

            migrationBuilder.DropColumn(
                name: "PriorLicenseNote",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "RequiredBasicTheoryLessonsOverride",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "RequiredBasicTheoryLessonsOverrideReason",
                table: "Students");
        }
    }
}
