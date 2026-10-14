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
    [Migration("20260713120000_AddEnterprisePayrollSettings")]
    public partial class AddEnterprisePayrollSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnterprisePayrollSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InssRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    RatRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    ThirdPartyRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    FgtsRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    PayrollDueDay = table.Column<int>(type: "int", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterprisePayrollSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterprisePayrollSettings_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_EnterpriseId",
                table: "EnterprisePayrollSettings",
                column: "EnterpriseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_Id_CreatedAt_UpdatedAt",
                table: "EnterprisePayrollSettings",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_Id_CreatedAt_UpdatedAt_IsActive",
                table: "EnterprisePayrollSettings",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnterprisePayrollSettings");
        }
    }
}
