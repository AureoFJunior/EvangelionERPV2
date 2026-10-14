using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderEnterpriseActiveCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Order_EnterpriseId",
                table: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_Order_EnterpriseId_IsActive_CreatedAt",
                table: "Order",
                columns: new[] { "EnterpriseId", "IsActive", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Order_EnterpriseId_IsActive_CreatedAt",
                table: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_Order_EnterpriseId",
                table: "Order",
                column: "EnterpriseId");
        }
    }
}
