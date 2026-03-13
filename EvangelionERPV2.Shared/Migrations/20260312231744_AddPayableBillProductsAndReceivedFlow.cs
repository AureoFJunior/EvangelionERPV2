using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPayableBillProductsAndReceivedFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProductsReceivedAt",
                table: "PayableBill",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayableBillProduct",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<double>(type: "float", nullable: false),
                    UnitValue = table.Column<double>(type: "float", nullable: false),
                    LineAmount = table.Column<double>(type: "float", nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayableBillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayableBillProduct", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayableBillProduct_PayableBill_PayableBillId",
                        column: x => x.PayableBillId,
                        principalTable: "PayableBill",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayableBillProduct_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_Id_CreatedAt_UpdatedAt",
                table: "PayableBillProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayableBillProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_PayableBillId",
                table: "PayableBillProduct",
                column: "PayableBillId");

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_PayableBillId_ProductId",
                table: "PayableBillProduct",
                columns: new[] { "PayableBillId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_ProductId",
                table: "PayableBillProduct",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayableBillProduct");

            migrationBuilder.DropColumn(
                name: "ProductsReceivedAt",
                table: "PayableBill");
        }
    }
}
