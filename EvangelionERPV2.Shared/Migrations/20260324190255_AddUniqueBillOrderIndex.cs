using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueBillOrderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto");

            migrationBuilder.CreateIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto");

            migrationBuilder.CreateIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto",
                column: "OrderId");
        }
    }
}
