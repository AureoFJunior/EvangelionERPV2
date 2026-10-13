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
    [Migration("20260821110000_AddEnterprisePeriodLock")]
    public partial class AddEnterprisePeriodLock : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable with no default: every existing tenant starts with nothing closed, which is
            // exactly today's behaviour. A non-null default would retroactively lock somebody's books.
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedThroughUtc",
                table: "Enterprise",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedThroughUtc",
                table: "Enterprise");
        }
    }
}
