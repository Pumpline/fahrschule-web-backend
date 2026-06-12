using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Ausbildungsplan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurriculumItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RequiredCount = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumItemClass",
                columns: table => new
                {
                    CurriculumItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseClassId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumItemClass", x => new { x.CurriculumItemId, x.LicenseClassId });
                    table.ForeignKey(
                        name: "FK_CurriculumItemClass_CurriculumItems_CurriculumItemId",
                        column: x => x.CurriculumItemId,
                        principalTable: "CurriculumItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurriculumItemClass_LicenseClasses_LicenseClassId",
                        column: x => x.LicenseClassId,
                        principalTable: "LicenseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumItemClass_LicenseClassId",
                table: "CurriculumItemClass",
                column: "LicenseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumItems_ItemKey_Version",
                table: "CurriculumItems",
                columns: new[] { "ItemKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumItems_Section",
                table: "CurriculumItems",
                column: "Section");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurriculumItemClass");

            migrationBuilder.DropTable(
                name: "CurriculumItems");
        }
    }
}
