using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TheorieAnwesenheitOhneSpeicher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TheoryAttendances");

            migrationBuilder.DropTable(
                name: "TheorySessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TheorySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurriculumItemKey = table.Column<Guid>(type: "uuid", nullable: false),
                    DateOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TopicSection = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TopicTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheorySessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TheoryAttendances",
                columns: table => new
                {
                    TheorySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TickedProgressItemId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryAttendances", x => new { x.TheorySessionId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_TheoryAttendances_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TheoryAttendances_TheorySessions_TheorySessionId",
                        column: x => x.TheorySessionId,
                        principalTable: "TheorySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TheoryAttendances_StudentId",
                table: "TheoryAttendances",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_TheorySessions_DateOn",
                table: "TheorySessions",
                column: "DateOn");
        }
    }
}
