using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260821120000_AddSupplierNFeAccessKey")]
    public partial class AddSupplierNFeAccessKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierNFeAccessKey",
                table: "PayableBill",
                type: "nvarchar(44)",
                maxLength: 44,
                nullable: true);

            // Filtered on the column, not on IsActive: a soft-deleted import must still block a
            // re-import of the same nota. Apply with sqlcmd -I — a filtered index fails with
            // Msg 1934 without QUOTED_IDENTIFIER on.
            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_EnterpriseId_SupplierNFeAccessKey",
                table: "PayableBill",
                columns: new[] { "EnterpriseId", "SupplierNFeAccessKey" },
                unique: true,
                filter: "[SupplierNFeAccessKey] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayableBill_EnterpriseId_SupplierNFeAccessKey",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "SupplierNFeAccessKey",
                table: "PayableBill");
        }
    }
}
