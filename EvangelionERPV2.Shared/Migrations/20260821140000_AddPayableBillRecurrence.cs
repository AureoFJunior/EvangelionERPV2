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
    [Migration("20260821140000_AddPayableBillRecurrence")]
    public partial class AddPayableBillRecurrence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurrenceMonths",
                table: "PayableBill",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceUntilUtc",
                table: "PayableBill",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceParentId",
                table: "PayableBill",
                type: "uniqueidentifier",
                nullable: true);

            // The sweep's idempotency: one child per parent. Apply with sqlcmd -I — a filtered
            // index fails with Msg 1934 without QUOTED_IDENTIFIER on.
            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_RecurrenceParentId",
                table: "PayableBill",
                column: "RecurrenceParentId",
                unique: true,
                filter: "[RecurrenceParentId] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayableBill_RecurrenceParentId",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "RecurrenceParentId",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "RecurrenceUntilUtc",
                table: "PayableBill");

            migrationBuilder.DropColumn(
                name: "RecurrenceMonths",
                table: "PayableBill");
        }
    }
}
