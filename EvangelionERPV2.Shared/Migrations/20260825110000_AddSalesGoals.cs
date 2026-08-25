using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Metas e comissão. One table and one rate column.
    //
    // The commission rate is decimal(5,4), not the global money precision: at decimal(18,2) a rate
    // of 0.035 would be stored as 0.04 and every seller would be paid the wrong number. Same trap
    // the payroll rates carry.
    //
    // The unique index is filtered on IsActive so a target that was removed and set again does not
    // collide with its own soft-deleted row. UserId is part of the key and nullable: SQL Server
    // treats nulls as equal in a unique index, which is exactly what keeps one enterprise-wide
    // target per competência.
    //
    // The [Migration] attribute is required for EF to discover this class — without it the
    // migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260825110000_AddSalesGoals")]
    public partial class AddSalesGoals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "User",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesGoal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesGoal", x => x.Id);
                    // Restrict on both: a target is a record of what was expected of somebody, and
                    // deleting the tenant or the seller must not quietly take that record with it.
                    table.ForeignKey(
                        name: "FK_SalesGoal_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesGoal_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesGoal_EnterpriseId_Year_Month",
                table: "SalesGoal",
                columns: new[] { "EnterpriseId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesGoal_EnterpriseId_UserId_Year_Month",
                table: "SalesGoal",
                columns: new[] { "EnterpriseId", "UserId", "Year", "Month" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SalesGoal_UserId",
                table: "SalesGoal",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SalesGoal");
            migrationBuilder.DropColumn(name: "CommissionRate", table: "User");
        }
    }
}
