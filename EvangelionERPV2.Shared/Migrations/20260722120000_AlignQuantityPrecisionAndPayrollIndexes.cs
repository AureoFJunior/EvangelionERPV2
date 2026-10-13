using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722120000_AlignQuantityPrecisionAndPayrollIndexes")]
    public partial class AlignQuantityPrecisionAndPayrollIndexes : Migration
    {
        // Two unrelated-looking but same-shaped corrections to what 20260721130000 and the
        // payroll migrations left behind:
        //
        // 1. The global decimal(18,2) convention was applied to quantities as well as money.
        //    UnitOfMeasure allows Kg/Litre/Meter, so 0.004 Kg persisted as 0.00. Quantities
        //    move to decimal(18,4); money stays at decimal(18,2). Widening the scale is
        //    non-destructive — no pre-flight check needed.
        //
        // 2. The payroll indexes were not filtered on [IsActive], so soft-deleted rows still
        //    occupied the unique slot, and the payroll competência index was not unique at all
        //    (nothing stopped two concurrent GeneratePayroll calls double-billing an employee).
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "OrderedProduct",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "PayableBillProduct",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "StorageQuantity",
                table: "Product",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.DropIndex(
                name: "IX_PayrollBillDetail_ReferenceYear_ReferenceMonth_EmployeeId",
                table: "PayrollBillDetail");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_ReferenceYear_ReferenceMonth_EmployeeId",
                table: "PayrollBillDetail",
                columns: new[] { "ReferenceYear", "ReferenceMonth", "EmployeeId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.DropIndex(
                name: "IX_EnterprisePayrollSettings_EnterpriseId",
                table: "EnterprisePayrollSettings");

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_EnterpriseId",
                table: "EnterprisePayrollSettings",
                column: "EnterpriseId",
                unique: true,
                filter: "[IsActive] = 1");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EnterprisePayrollSettings_EnterpriseId",
                table: "EnterprisePayrollSettings");

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_EnterpriseId",
                table: "EnterprisePayrollSettings",
                column: "EnterpriseId",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_PayrollBillDetail_ReferenceYear_ReferenceMonth_EmployeeId",
                table: "PayrollBillDetail");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_ReferenceYear_ReferenceMonth_EmployeeId",
                table: "PayrollBillDetail",
                columns: new[] { "ReferenceYear", "ReferenceMonth", "EmployeeId" });

            // Narrowing back to scale 2 discards the fourth decimal place; SQL Server rounds
            // rather than failing, so this is lossy but does not abort.
            migrationBuilder.AlterColumn<decimal>(
                name: "StorageQuantity",
                table: "Product",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "PayableBillProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "OrderedProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");
        }
    }
}
