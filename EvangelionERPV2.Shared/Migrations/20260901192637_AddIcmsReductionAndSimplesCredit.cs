using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddIcmsReductionAndSimplesCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReducaoBaseIcms",
                table: "Product",
                type: "decimal(5,4)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AliquotaCreditoSn",
                table: "EnterpriseFiscalSettings",
                type: "decimal(5,4)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReducaoBaseIcms",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "AliquotaCreditoSn",
                table: "EnterpriseFiscalSettings");
        }
    }
}
