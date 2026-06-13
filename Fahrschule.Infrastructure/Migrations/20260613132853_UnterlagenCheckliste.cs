using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnterlagenCheckliste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentChecklistItems",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPresent = table.Column<bool>(type: "boolean", nullable: false),
                    PresentedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChecklistItems", x => new { x.StudentId, x.DocumentCatalogItemId });
                    table.ForeignKey(
                        name: "FK_DocumentChecklistItems_DocumentCatalogItems_DocumentCatalog~",
                        column: x => x.DocumentCatalogItemId,
                        principalTable: "DocumentCatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentChecklistItems_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChecklistItems_DocumentCatalogItemId",
                table: "DocumentChecklistItems",
                column: "DocumentCatalogItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentChecklistItems");
        }
    }
}
