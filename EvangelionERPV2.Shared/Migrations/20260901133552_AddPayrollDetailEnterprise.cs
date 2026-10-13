using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollDetailEnterprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnterpriseId",
                table: "PayrollBillDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill before the column means anything. A row left at Guid.Empty is invisible to
            // every request once the convention filter picks the column up, and the row that goes
            // missing here is the orphan detail payroll generation has to retire before it can bill
            // that employee again — so it would surface as a unique-index violation, not as a gap.
            // The bill is the primary source (it is what the old post-filter used); the employee
            // catches any detail whose bill row is gone entirely.
            // Through EXEC on purpose: the idempotent script puts the AddColumn above and these
            // statements in one batch, and SQL Server parses a batch whole — a direct UPDATE
            // naming EnterpriseId fails with "Invalid column name" before the ALTER has run.
            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE detail
                    SET detail.EnterpriseId = bill.EnterpriseId
                    FROM PayrollBillDetail detail
                    INNER JOIN PayableBill bill ON bill.Id = detail.PayableBillId;');");

            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE detail
                    SET detail.EnterpriseId = employee.EnterpriseId
                    FROM PayrollBillDetail detail
                    INNER JOIN Employee employee ON employee.Id = detail.EmployeeId
                    WHERE detail.EnterpriseId = ''00000000-0000-0000-0000-000000000000''
                      AND employee.EnterpriseId IS NOT NULL;');");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_EnterpriseId_ReferenceYear_ReferenceMonth",
                table: "PayrollBillDetail",
                columns: new[] { "EnterpriseId", "ReferenceYear", "ReferenceMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollBillDetail_EnterpriseId_ReferenceYear_ReferenceMonth",
                table: "PayrollBillDetail");

            migrationBuilder.DropColumn(
                name: "EnterpriseId",
                table: "PayrollBillDetail");
        }
    }
}
