using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TerminStundeVerknuepfung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LessonId",
                table: "CalendarEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_LessonId",
                table: "CalendarEvents",
                column: "LessonId");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarEvents_Lessons_LessonId",
                table: "CalendarEvents",
                column: "LessonId",
                principalTable: "Lessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarEvents_Lessons_LessonId",
                table: "CalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEvents_LessonId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "LessonId",
                table: "CalendarEvents");
        }
    }
}
