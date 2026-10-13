using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublishedOnStorefront",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsListedOnStorefront",
                table: "Enterprise",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontDeliveryDaysMax",
                table: "Enterprise",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontDeliveryDaysMin",
                table: "Enterprise",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StorefrontDescription",
                table: "Enterprise",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontHandlingDays",
                table: "Enterprise",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorefrontListedAt",
                table: "Enterprise",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorefrontLogoUrl",
                table: "Enterprise",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StorefrontShippingFee",
                table: "Enterprise",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StorefrontSlug",
                table: "Enterprise",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_IsPublishedOnStorefront_EnterpriseId",
                table: "Product",
                columns: new[] { "IsPublishedOnStorefront", "EnterpriseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_StorefrontSlug",
                table: "Enterprise",
                column: "StorefrontSlug",
                unique: true,
                filter: "[StorefrontSlug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Product_IsPublishedOnStorefront_EnterpriseId",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Enterprise_StorefrontSlug",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "IsPublishedOnStorefront",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "IsListedOnStorefront",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontDeliveryDaysMax",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontDeliveryDaysMin",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontDescription",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontHandlingDays",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontListedAt",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontLogoUrl",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontShippingFee",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontSlug",
                table: "Enterprise");
        }
    }
}
