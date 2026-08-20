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
    [Migration("20260820130000_AddVacationPayLinks")]
    public partial class AddVacationPayLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults to 0 so every existing payroll row reads as "nothing drawn down yet",
            // which is exactly what is true of them.
            migrationBuilder.AddColumn<decimal>(
                name: "VacationProvisionConsumed",
                table: "PayrollBillDetail",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeVacationAccrualId",
                table: "PayableBill",
                type: "uniqueidentifier",
                nullable: true);

            // Filtered: only férias bills carry the link, and every other payable would otherwise
            // pile NULLs into it.
            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_EmployeeVacationAccrualId",
                table: "PayableBill",
                column: "EmployeeVacationAccrualId",
                filter: "[EmployeeVacationAccrualId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PayableBill_EmployeeVacationAccrual_EmployeeVacationAccrualId",
                table: "PayableBill",
                column: "EmployeeVacationAccrualId",
                principalTable: "EmployeeVacationAccrual",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayableBill_EmployeeVacationAccrual_EmployeeVacationAccrualId",
                table: "PayableBill");

            migrationBuilder.DropIndex(
                name: "IX_PayableBill_EmployeeVacationAccrualId",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "EmployeeVacationAccrualId",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "VacationProvisionConsumed",
                table: "PayrollBillDetail");
        }
    }
}
