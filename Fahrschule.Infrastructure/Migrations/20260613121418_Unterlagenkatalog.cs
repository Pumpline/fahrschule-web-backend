using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fahrschule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Unterlagenkatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpiryDateRequired = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_DocumentCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCatalogItemClass",
                columns: table => new
                {
                    DocumentCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseClassId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCatalogItemClass", x => new { x.DocumentCatalogItemId, x.LicenseClassId });
                    table.ForeignKey(
                        name: "FK_DocumentCatalogItemClass_DocumentCatalogItems_DocumentCatal~",
                        column: x => x.DocumentCatalogItemId,
                        principalTable: "DocumentCatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentCatalogItemClass_LicenseClasses_LicenseClassId",
                        column: x => x.LicenseClassId,
                        principalTable: "LicenseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCatalogItemClass_LicenseClassId",
                table: "DocumentCatalogItemClass",
                column: "LicenseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCatalogItems_SortOrder",
                table: "DocumentCatalogItems",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentCatalogItemClass");

            migrationBuilder.DropTable(
                name: "DocumentCatalogItems");
        }
    }
}
