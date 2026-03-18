using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class FixPayableBillProductActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayableBillProduct_PayableBillId_ProductId",
                table: "PayableBillProduct");

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_PayableBillId_ProductId",
                table: "PayableBillProduct",
                columns: new[] { "PayableBillId", "ProductId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayableBillProduct_PayableBillId_ProductId",
                table: "PayableBillProduct");

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_PayableBillId_ProductId",
                table: "PayableBillProduct",
                columns: new[] { "PayableBillId", "ProductId" },
                unique: true);
        }
    }
}
