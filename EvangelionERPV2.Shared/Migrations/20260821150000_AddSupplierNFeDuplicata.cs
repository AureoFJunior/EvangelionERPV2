using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260821150000_AddSupplierNFeDuplicata")]
    public partial class AddSupplierNFeDuplicata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierNFeDuplicata",
                table: "PayableBill",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            // The key widens from the nota to the instalment: one payable per duplicata. Rows
            // imported before this migration keep a null duplicata, which the unique index still
            // treats as a value, so they go on blocking a re-import of their own nota.
            migrationBuilder.DropIndex(
                name: "IX_PayableBill_EnterpriseId_SupplierNFeAccessKey",
                table: "PayableBill");

            // Apply with sqlcmd -I — a filtered index fails with Msg 1934 without QUOTED_IDENTIFIER.
            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_EnterpriseId_SupplierNFeAccessKey_SupplierNFeDuplicata",
                table: "PayableBill",
                columns: new[] { "EnterpriseId", "SupplierNFeAccessKey", "SupplierNFeDuplicata" },
                unique: true,
                filter: "[SupplierNFeAccessKey] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayableBill_EnterpriseId_SupplierNFeAccessKey_SupplierNFeDuplicata",
                table: "PayableBill");

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_EnterpriseId_SupplierNFeAccessKey",
                table: "PayableBill",
                columns: new[] { "EnterpriseId", "SupplierNFeAccessKey" },
                unique: true,
                filter: "[SupplierNFeAccessKey] IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "SupplierNFeDuplicata",
                table: "PayableBill");
        }
    }
}
