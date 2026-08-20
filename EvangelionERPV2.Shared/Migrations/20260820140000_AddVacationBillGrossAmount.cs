using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260820140000_AddVacationBillGrossAmount")]
    public partial class AddVacationBillGrossAmount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A férias bill's Amount is netted by the provision already accrued for it
            // (docs/employee/SPEC-hr-module.md §4 P5a), so the gross has to be stored or the
            // drawdown would allocate against its own output.
            //
            // Nullable rather than defaulted: NULL reads as "gross equals Amount", which is true of
            // every bill written before this column existed and of every non-férias payable.
            migrationBuilder.AddColumn<decimal>(
                name: "GrossAmount",
                table: "PayableBill",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GrossAmount",
                table: "PayableBill");
        }
    }
}
