using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceHistoryAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CompareAtPrice",
                table: "Product",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromotionAttestedAt",
                table: "Product",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionAttestedByUserId",
                table: "Product",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromotionEndsAt",
                table: "Product",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromotionStartedAt",
                table: "Product",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductPriceHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    From = table.Column<DateTime>(type: "datetime2", nullable: false),
                    To = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceHistory_ProductId_From",
                table: "ProductPriceHistory",
                columns: new[] { "ProductId", "From" });

            // Every product starts its history at today's price, so a "de" price can be proved only
            // after 7 real days (SPEC-limen-pricing-rules §2.2).
            migrationBuilder.Sql(@"
                INSERT INTO ProductPriceHistory (Id, ProductId, EnterpriseId, Price, [From], [To], CreatedAt, UpdatedAt, IsActive)
                SELECT NEWID(), p.Id, p.EnterpriseId, p.DefaultValue, SYSUTCDATETIME(), NULL, SYSUTCDATETIME(), NULL, 1
                FROM Product p
                WHERE p.EnterpriseId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPriceHistory");

            migrationBuilder.DropColumn(
                name: "CompareAtPrice",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "PromotionAttestedAt",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "PromotionAttestedByUserId",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "PromotionEndsAt",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "PromotionStartedAt",
                table: "Product");
        }
    }
}
