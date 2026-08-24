using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260821130000_AddCustomerCreditLimit")]
    public partial class AddCustomerCreditLimit : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable with no default: null is "no limit", so every existing customer keeps
            // today's behaviour. A default of zero would put the whole book on cash only.
            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Customer",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Customer");
        }
    }
}
