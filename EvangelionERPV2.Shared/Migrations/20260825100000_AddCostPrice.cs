using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Custo e margem. Two nullable money columns and nothing else.
    //
    // Both are nullable on purpose. A NOT NULL DEFAULT 0 would backfill every existing product and
    // every order line ever placed with a cost of zero, and every margin report would then claim
    // the company earns 100% on everything it has ever sold. Null means "unknown", and OrderMargin
    // answers null for the whole order when any of its lines is.
    //
    // OrderedProduct.UnitCost is a snapshot written once at line creation, never a join back to
    // Product.CostPrice: editing a product's cost must not rewrite the margin of a past order.
    //
    // The [Migration] attribute is required for EF to discover this class — without it the
    // migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260825100000_AddCostPrice")]
    public partial class AddCostPrice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostPrice",
                table: "Product",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "OrderedProduct",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UnitCost", table: "OrderedProduct");
            migrationBuilder.DropColumn(name: "CostPrice", table: "Product");
        }
    }
}
