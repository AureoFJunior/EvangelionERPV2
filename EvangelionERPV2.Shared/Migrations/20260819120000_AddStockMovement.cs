using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Append-only ledger behind Product.StorageQuantity. Purely additive: no existing table is
    // touched and no backfill is attempted, so rows written before this migration simply have no
    // history — inventing movements for them would put fiction in an audit table.
    //
    // Quantity and BalanceAfter are decimal(18,4), matching Product.StorageQuantity, because a
    // product measured in Kg moves in fractions the conventional decimal(18,2) would truncate.
    //
    // The [Migration] attribute is required for EF to discover this class at Database.Migrate()
    // time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260819120000_AddStockMovement")]
    public partial class AddStockMovement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovement", x => x.Id);

                    // Restrict, not Cascade: deleting a product out from under its movements would
                    // destroy the only record of how its count got where it is.
                    table.ForeignKey(
                        name: "FK_StockMovement_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // EnterpriseId leads so the replenishment window scan seeks on the tenant, and a
            // per-product history lookup seeks on (EnterpriseId, ProductId).
            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_EnterpriseId_ProductId_CreatedAt",
                table: "StockMovement",
                columns: ["EnterpriseId", "ProductId", "CreatedAt"]);

            // EF's own foreign-key index. Not redundant with the composite above, which does not
            // lead with ProductId.
            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_ProductId",
                table: "StockMovement",
                column: "ProductId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StockMovement");
        }
    }
}
