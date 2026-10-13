using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260721130000_ConvertMoneyColumnsToDecimal")]
    public partial class ConvertMoneyColumnsToDecimal : Migration
    {
        // Money and the quantities that multiply into it were stored as SQL `float`, so every
        // total was accumulated in binary floating point and could not represent values like
        // 0.10 exactly. decimal(18,2) matches what PayrollBillDetail already used.
        // Dimensionless values (ConfidenceScore, PriorityScore, SignalValue) stay float.
        //
        // PRE-FLIGHT: converting float to decimal(18,2) raises "Arithmetic overflow" if any
        // existing row is outside the new range, which aborts the whole migration. Check first:
        //
        //   SELECT 'PayableBill.Amount' AS Col, COUNT(*) FROM PayableBill WHERE ABS(Amount) >= 1E16
        //   UNION ALL SELECT 'Order.TotalValue', COUNT(*) FROM [Order] WHERE ABS(TotalValue) >= 1E16
        //   UNION ALL SELECT 'Enterprise.CurrentBalance', COUNT(*) FROM Enterprise WHERE ABS(CurrentBalance) >= 1E16
        //   UNION ALL SELECT 'Product.DefaultValue', COUNT(*) FROM Product WHERE ABS(DefaultValue) >= 1E16;
        //
        // Any non-zero count is corrupt data that must be corrected before applying this.
        // Values are also rounded to 2 decimal places; sub-cent float noise is discarded.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "OrderedProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "OrderedProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "CurrentBalance",
                table: "Enterprise",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 0.0,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "PayableBill",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "PayableBillProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitValue",
                table: "PayableBillProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "LineAmount",
                table: "PayableBillProduct",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultValue",
                table: "Product",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "StorageQuantity",
                table: "Product",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "FinalProjectedBalance",
                table: "ForecastSimulationLog",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedRevenueImpact",
                table: "Opportunity",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedMarginImpact",
                table: "Opportunity",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedCashImpact",
                table: "Opportunity",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal?>(
                name: "RealRevenueImpact",
                table: "OpportunityFeedback",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(double?),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal?>(
                name: "RealMarginImpact",
                table: "OpportunityFeedback",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(double?),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal?>(
                name: "RealCashImpact",
                table: "OpportunityFeedback",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(double?),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalValue",
                table: "Order",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Boleto",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalValue",
                table: "NFeDocument",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Quantity",
                table: "OrderedProduct",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "Value",
                table: "OrderedProduct",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "CurrentBalance",
                table: "Enterprise",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m,
                defaultValue: 0.0);

            migrationBuilder.AlterColumn<double>(
                name: "Amount",
                table: "PayableBill",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "Quantity",
                table: "PayableBillProduct",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "UnitValue",
                table: "PayableBillProduct",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "LineAmount",
                table: "PayableBillProduct",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "DefaultValue",
                table: "Product",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "StorageQuantity",
                table: "Product",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "FinalProjectedBalance",
                table: "ForecastSimulationLog",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "EstimatedRevenueImpact",
                table: "Opportunity",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "EstimatedMarginImpact",
                table: "Opportunity",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "EstimatedCashImpact",
                table: "Opportunity",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double?>(
                name: "RealRevenueImpact",
                table: "OpportunityFeedback",
                type: "float",
                nullable: true,
                oldClrType: typeof(decimal?),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double?>(
                name: "RealMarginImpact",
                table: "OpportunityFeedback",
                type: "float",
                nullable: true,
                oldClrType: typeof(decimal?),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double?>(
                name: "RealCashImpact",
                table: "OpportunityFeedback",
                type: "float",
                nullable: true,
                oldClrType: typeof(decimal?),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "TotalValue",
                table: "Order",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "Amount",
                table: "Boleto",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<double>(
                name: "TotalValue",
                table: "NFeDocument",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");
        }
    }
}
