using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPdvModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Product_EnterpriseId",
                table: "Product");

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "Product",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerminalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CountedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashSession_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovement_CashSession_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PdvSale",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientSaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CashSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerminalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SurchargeAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SoldAtLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdvSale", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdvSale_CashSession_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PdvSale_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PdvSalePayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PdvSaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ChangeGiven = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AuthorizationCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Installments = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdvSalePayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdvSalePayment_PdvSale_PdvSaleId",
                        column: x => x.PdvSaleId,
                        principalTable: "PdvSale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Product_EnterpriseId_Cean",
                table: "Product",
                columns: new[] { "EnterpriseId", "Cean" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_EnterpriseId_Sku",
                table: "Product",
                columns: new[] { "EnterpriseId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovement_CashSessionId_CreatedAt",
                table: "CashMovement",
                columns: new[] { "CashSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovement_EnterpriseId_ClientMovementId",
                table: "CashMovement",
                columns: new[] { "EnterpriseId", "ClientMovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashSession_EnterpriseId_ClientSessionId",
                table: "CashSession",
                columns: new[] { "EnterpriseId", "ClientSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashSession_EnterpriseId_TerminalId",
                table: "CashSession",
                columns: new[] { "EnterpriseId", "TerminalId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashSession_EnterpriseId_TerminalId_Status",
                table: "CashSession",
                columns: new[] { "EnterpriseId", "TerminalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSession_UserId",
                table: "CashSession",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PdvSale_CashSessionId",
                table: "PdvSale",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PdvSale_EnterpriseId_ClientSaleId",
                table: "PdvSale",
                columns: new[] { "EnterpriseId", "ClientSaleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PdvSale_OrderId",
                table: "PdvSale",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PdvSalePayment_PdvSaleId",
                table: "PdvSalePayment",
                column: "PdvSaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashMovement");

            migrationBuilder.DropTable(
                name: "PdvSalePayment");

            migrationBuilder.DropTable(
                name: "PdvSale");

            migrationBuilder.DropTable(
                name: "CashSession");

            migrationBuilder.DropIndex(
                name: "IX_Product_EnterpriseId_Cean",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Product_EnterpriseId_Sku",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "Product");

            migrationBuilder.CreateIndex(
                name: "IX_Product_EnterpriseId",
                table: "Product",
                column: "EnterpriseId");
        }
    }
}
