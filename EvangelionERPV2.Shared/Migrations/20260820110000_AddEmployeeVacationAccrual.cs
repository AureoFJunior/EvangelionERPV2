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
    [Migration("20260820110000_AddEmployeeVacationAccrual")]
    public partial class AddEmployeeVacationAccrual : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeVacationAccrual",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcquisitionStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcquisitionEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcessionDeadline = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntitledDays = table.Column<int>(type: "int", nullable: false),
                    ScheduledDays = table.Column<int>(type: "int", nullable: false),
                    TakenDays = table.Column<int>(type: "int", nullable: false),
                    SoldDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeVacationAccrual", x => x.Id);
                    // Restrict: an accrual is the evidence behind a férias payment, and a lapsed one
                    // is evidence of a debt owed in dobro. It outlives any hard-delete attempt.
                    table.ForeignKey(
                        name: "FK_EmployeeVacationAccrual_Employee_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeVacationAccrual_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeVacationAccrual_EmployeeId",
                table: "EmployeeVacationAccrual",
                column: "EmployeeId");

            // One employee's ledger, oldest period first — the order entitlement must be consumed in.
            migrationBuilder.CreateIndex(
                name: "IX_EmployeeVacationAccrual_EnterpriseId_EmployeeId_AcquisitionStart",
                table: "EmployeeVacationAccrual",
                columns: new[] { "EnterpriseId", "EmployeeId", "AcquisitionStart" });

            // The sweeper's query: this tenant's accruals whose concessivo deadline is near or past.
            // Without it the lapse check table-scans every accrual in the installation.
            migrationBuilder.CreateIndex(
                name: "IX_EmployeeVacationAccrual_EnterpriseId_ConcessionDeadline",
                table: "EmployeeVacationAccrual",
                columns: new[] { "EnterpriseId", "ConcessionDeadline" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeVacationAccrual");
        }
    }
}
