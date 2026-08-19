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
    [Migration("20260713130000_AddPayrollBillDetail")]
    public partial class AddPayrollBillDetail : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollBillDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceYear = table.Column<int>(type: "int", nullable: false),
                    ReferenceMonth = table.Column<int>(type: "int", nullable: false),
                    GrossSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ThirteenthSalaryProvision = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VacationProvision = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InssAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ThirdPartyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FgtsAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayableBillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBillDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBillDetail_PayableBill_PayableBillId",
                        column: x => x.PayableBillId,
                        principalTable: "PayableBill",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollBillDetail_Employee_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_PayableBillId",
                table: "PayrollBillDetail",
                column: "PayableBillId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_EmployeeId",
                table: "PayrollBillDetail",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_ReferenceYear_ReferenceMonth_EmployeeId",
                table: "PayrollBillDetail",
                columns: new[] { "ReferenceYear", "ReferenceMonth", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_Id_CreatedAt_UpdatedAt",
                table: "PayrollBillDetail",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayrollBillDetail",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollBillDetail");
        }
    }
}
