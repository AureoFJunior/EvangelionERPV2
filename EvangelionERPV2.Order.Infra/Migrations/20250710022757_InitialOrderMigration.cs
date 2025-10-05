using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.OrderModule.Infra.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrderMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Enterprise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Adress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShouldSendMonthlyBilling = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enterprise", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultValue = table.Column<double>(type: "float", nullable: false),
                    StorageQuantity = table.Column<double>(type: "float", nullable: false),
                    IsExternal = table.Column<bool>(type: "bit", nullable: false),
                    IsService = table.Column<bool>(type: "bit", nullable: false),
                    PictureAdress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Adress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Order",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Payday = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalValue = table.Column<double>(type: "float", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Order", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Order_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Order_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderedProduct",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<double>(type: "float", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderedProduct", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderedProduct_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderedProduct_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CreatedAt_UpdatedAt_IsActive_Name",
                table: "Customer",
                columns: new[] { "CreatedAt", "UpdatedAt", "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_EnterpriseId",
                table: "Customer",
                column: "EnterpriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Id_CreatedAt_UpdatedAt",
                table: "Customer",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Customer",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Name",
                table: "Customer",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_CreatedAt_UpdatedAt_IsActive_Name",
                table: "Enterprise",
                columns: new[] { "CreatedAt", "UpdatedAt", "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_Id_CreatedAt_UpdatedAt",
                table: "Enterprise",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Enterprise",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_Name",
                table: "Enterprise",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Order_CustomerId",
                table: "Order",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Order_EnterpriseId",
                table: "Order",
                column: "EnterpriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Order_Id_CreatedAt_UpdatedAt",
                table: "Order",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Order_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Order",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderedProduct_Id_CreatedAt_UpdatedAt",
                table: "OrderedProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderedProduct_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OrderedProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderedProduct_OrderId",
                table: "OrderedProduct",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderedProduct_ProductId",
                table: "OrderedProduct",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_Id_CreatedAt_UpdatedAt",
                table: "Product",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Product",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderedProduct");

            migrationBuilder.DropTable(
                name: "Order");

            migrationBuilder.DropTable(
                name: "Product");

            migrationBuilder.DropTable(
                name: "Customer");

            migrationBuilder.DropTable(
                name: "Enterprise");
        }
    }
}
