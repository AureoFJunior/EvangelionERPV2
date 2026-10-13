using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutInstallments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CetAnnual",
                table: "StorefrontCheckout",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InstallmentValue",
                table: "StorefrontCheckout",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Installments",
                table: "StorefrontCheckout",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestMonthly",
                table: "StorefrontCheckout",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWithInterest",
                table: "StorefrontCheckout",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CetAnnual",
                table: "StorefrontCheckout");

            migrationBuilder.DropColumn(
                name: "InstallmentValue",
                table: "StorefrontCheckout");

            migrationBuilder.DropColumn(
                name: "Installments",
                table: "StorefrontCheckout");

            migrationBuilder.DropColumn(
                name: "InterestMonthly",
                table: "StorefrontCheckout");

            migrationBuilder.DropColumn(
                name: "TotalWithInterest",
                table: "StorefrontCheckout");
        }
    }
}
