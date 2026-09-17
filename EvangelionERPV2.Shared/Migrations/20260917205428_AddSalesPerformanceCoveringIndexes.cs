using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPerformanceCoveringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Order_EnterpriseId_IsActive_CreatedAt",
                table: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_Order_EnterpriseId_IsActive_CreatedAt",
                table: "Order",
                columns: new[] { "EnterpriseId", "IsActive", "CreatedAt" })
                .Annotation("SqlServer:Include", new[] { "UserId", "Status", "RefundedAt", "Payday", "TotalValue" });

            migrationBuilder.CreateIndex(
                name: "IX_Boleto_OrderId_IsActive_Status",
                table: "Boleto",
                columns: new[] { "OrderId", "IsActive", "Status" })
                .Annotation("SqlServer:Include", new[] { "CreatedAt", "Amount", "PaidAmount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Order_EnterpriseId_IsActive_CreatedAt",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_Boleto_OrderId_IsActive_Status",
                table: "Boleto");

            migrationBuilder.CreateIndex(
                name: "IX_Order_EnterpriseId_IsActive_CreatedAt",
                table: "Order",
                columns: new[] { "EnterpriseId", "IsActive", "CreatedAt" });
        }
    }
}
