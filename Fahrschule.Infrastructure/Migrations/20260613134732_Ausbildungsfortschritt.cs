using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Ausbildungsfortschritt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentProgressItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumItemKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CurriculumItemVersion = table.Column<int>(type: "integer", nullable: false),
                    Section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RequiredCount = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentProgressItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentProgressItems_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentProgressEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentProgressItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentProgressEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentProgressEntry_StudentProgressItems_StudentProgressIt~",
                        column: x => x.StudentProgressItemId,
                        principalTable: "StudentProgressItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentProgressItemClass",
                columns: table => new
                {
                    StudentProgressItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseClassId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentProgressItemClass", x => new { x.StudentProgressItemId, x.LicenseClassId });
                    table.ForeignKey(
                        name: "FK_StudentProgressItemClass_LicenseClasses_LicenseClassId",
                        column: x => x.LicenseClassId,
                        principalTable: "LicenseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentProgressItemClass_StudentProgressItems_StudentProgre~",
                        column: x => x.StudentProgressItemId,
                        principalTable: "StudentProgressItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentProgressEntry_StudentProgressItemId",
                table: "StudentProgressEntry",
                column: "StudentProgressItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProgressItemClass_LicenseClassId",
                table: "StudentProgressItemClass",
                column: "LicenseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProgressItems_StudentId",
                table: "StudentProgressItems",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentProgressEntry");

            migrationBuilder.DropTable(
                name: "StudentProgressItemClass");

            migrationBuilder.DropTable(
                name: "StudentProgressItems");
        }
    }
}
