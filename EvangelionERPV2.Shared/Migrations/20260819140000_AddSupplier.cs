using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260819140000_AddSupplier")]
    public partial class AddSupplier : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Adress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Document = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supplier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Supplier_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_EnterpriseId",
                table: "Supplier",
                column: "EnterpriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_Name",
                table: "Supplier",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_CreatedAt_UpdatedAt_IsActive_Name",
                table: "Supplier",
                columns: new[] { "CreatedAt", "UpdatedAt", "IsActive", "Name" });

            // Nullable: payables predate suppliers, and payroll or tax bills have no
            // counterparty, so existing rows stay valid without a placeholder supplier.
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "PayableBill",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_SupplierId",
                table: "PayableBill",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_PayableBill_Supplier_SupplierId",
                table: "PayableBill",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayableBill_Supplier_SupplierId",
                table: "PayableBill");

            migrationBuilder.DropIndex(
                name: "IX_PayableBill_SupplierId",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "PayableBill");

            migrationBuilder.DropTable(
                name: "Supplier");
        }
    }
}
