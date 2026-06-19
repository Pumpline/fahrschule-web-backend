using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StundenFortschrittKopplung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManuallyCompleted",
                table: "StudentProgressItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LessonId",
                table: "StudentProgressEntry",
                type: "uuid",
                nullable: true);

            // Existing links were full covering sessions → default them to "counts".
            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardRequirement",
                table: "LessonItem",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentProgressEntry_LessonId",
                table: "StudentProgressEntry",
                column: "LessonId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentProgressEntry_Lessons_LessonId",
                table: "StudentProgressEntry",
                column: "LessonId",
                principalTable: "Lessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Data migration: a SIMPLE point that is currently completed but is
            // NOT covered by any lesson was ticked manually (or via theory
            // attendance / Anrechnung). Mark it so the new lesson-driven
            // recompute keeps it "done" instead of clearing it.
            migrationBuilder.Sql(
                """
                UPDATE "StudentProgressItems"
                SET "ManuallyCompleted" = true
                WHERE "IsCompleted" = true
                  AND "RequiredCount" IS NULL
                  AND "Id" NOT IN (SELECT "StudentProgressItemId" FROM "LessonItem");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentProgressEntry_Lessons_LessonId",
                table: "StudentProgressEntry");

            migrationBuilder.DropIndex(
                name: "IX_StudentProgressEntry_LessonId",
                table: "StudentProgressEntry");

            migrationBuilder.DropColumn(
                name: "ManuallyCompleted",
                table: "StudentProgressItems");

            migrationBuilder.DropColumn(
                name: "LessonId",
                table: "StudentProgressEntry");

            migrationBuilder.DropColumn(
                name: "CountsTowardRequirement",
                table: "LessonItem");
        }
    }
}
