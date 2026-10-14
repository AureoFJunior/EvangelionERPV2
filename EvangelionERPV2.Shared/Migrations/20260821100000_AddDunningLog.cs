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
    [Migration("20260821100000_AddDunningLog")]
    public partial class AddDunningLog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShouldSendDunning",
                table: "Enterprise",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DunningLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OffsetDays = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DunningLog", x => x.Id);
                    // Cascade from Order: the log only means anything next to the receivable it
                    // chased, and orders are soft-deleted in this system anyway.
                    table.ForeignKey(
                        name: "FK_DunningLog_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    // Restrict on the tenant, so deleting an enterprise cannot quietly erase the
                    // evidence of what its customers were told.
                    table.ForeignKey(
                        name: "FK_DunningLog_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The idempotency key. Not filtered on IsActive: a soft-deleted row that let the
            // sweeper re-send would defeat the whole point of the table.
            migrationBuilder.CreateIndex(
                name: "IX_DunningLog_EnterpriseId_OrderId_OffsetDays",
                table: "DunningLog",
                columns: new[] { "EnterpriseId", "OrderId", "OffsetDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DunningLog_OrderId",
                table: "DunningLog",
                column: "OrderId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DunningLog");

            migrationBuilder.DropColumn(
                name: "ShouldSendDunning",
                table: "Enterprise");
        }
    }
}
