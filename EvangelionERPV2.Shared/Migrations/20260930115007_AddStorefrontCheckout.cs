using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "Order",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Order",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "Order",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "Order",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippedAt",
                table: "Order",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingValue",
                table: "Order",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ExternalShopperId",
                table: "Customer",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StorefrontCheckout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    TotalValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    QuoteJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AddressJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutcomesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PixCode = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PixExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeclineCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Simulated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontCheckout", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Order_EnterpriseId_ExternalReference",
                table: "Order",
                columns: new[] { "EnterpriseId", "ExternalReference" },
                unique: true,
                filter: "[ExternalReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Order_ExternalReference",
                table: "Order",
                column: "ExternalReference",
                filter: "[ExternalReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_EnterpriseId_ExternalShopperId",
                table: "Customer",
                columns: new[] { "EnterpriseId", "ExternalShopperId" },
                filter: "[ExternalShopperId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCheckout_Email",
                table: "StorefrontCheckout",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCheckout_ShopperId",
                table: "StorefrontCheckout",
                column: "ShopperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorefrontCheckout");

            migrationBuilder.DropIndex(
                name: "IX_Order_EnterpriseId_ExternalReference",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_Order_ExternalReference",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_Customer_EnterpriseId_ExternalShopperId",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "ShippedAt",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "ShippingValue",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "ExternalShopperId",
                table: "Customer");
        }
    }
}
