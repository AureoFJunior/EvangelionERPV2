using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261013120000_AddProductVariantGrouping")]
    public partial class AddProductVariantGrouping : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VariantGroupId",
                table: "Product",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabel",
                table: "Product",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_VariantGroupId_VariantLabel",
                table: "Product",
                columns: new[] { "VariantGroupId", "VariantLabel" },
                unique: true,
                filter: "[IsActive] = 1 AND [VariantGroupId] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Product_VariantGroupId_VariantLabel",
                table: "Product");

            migrationBuilder.DropColumn(name: "VariantLabel", table: "Product");
            migrationBuilder.DropColumn(name: "VariantGroupId", table: "Product");
        }
    }
}
