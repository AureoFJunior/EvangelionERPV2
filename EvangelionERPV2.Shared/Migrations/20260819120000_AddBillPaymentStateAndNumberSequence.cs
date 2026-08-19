using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Payment state on the boleto, plus the NossoNumero counter table.
    //
    // Additive except for one index swap: Status/PaidAt/PaidAmount/SettlementProtocol are new
    // columns, BillNumberSequence is a new table, and IX_Boleto_OrderId is rebuilt with a filter.
    // Existing rows take Status = 1 (Registered) from the column default, which is what they are —
    // every boleto issued so far is open and unsettled, because nothing could ever settle one.
    //
    // The filter "[Status] <> 4" is the whole point of the rebuild: the unfiltered unique index
    // meant one order got exactly one boleto for all time, so a wrong due date was unrecoverable
    // through the API. Excluding cancelled rows lets a reissue supersede a title instead of the
    // API having to mutate a document a payer may already be holding.
    //
    // BillNumberSequence's unique index is deliberately NOT filtered on IsActive: a soft-deleted
    // counter that let a replacement row restart at the range floor would re-issue NossoNumero
    // values already handed to payers.
    //
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260819120000_AddBillPaymentStateAndNumberSequence")]
    public partial class AddBillPaymentStateAndNumberSequence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Boleto",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Boleto",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Boleto",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementProtocol",
                table: "Boleto",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto");

            migrationBuilder.CreateIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto",
                column: "OrderId",
                unique: true,
                filter: "[Status] <> 4");

            migrationBuilder.CreateTable(
                name: "BillNumberSequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankCode = table.Column<int>(type: "int", nullable: false),
                    Wallet = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Ceiling = table.Column<long>(type: "bigint", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillNumberSequence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillNumberSequence_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillNumberSequence_EnterpriseId_BankCode_Wallet",
                table: "BillNumberSequence",
                columns: new[] { "EnterpriseId", "BankCode", "Wallet" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillNumberSequence");

            migrationBuilder.DropIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto");

            migrationBuilder.DropColumn(
                name: "SettlementProtocol",
                table: "Boleto");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Boleto");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Boleto");

            // Dropped after the index, which filters on it.
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Boleto");

            migrationBuilder.CreateIndex(
                name: "IX_Boleto_OrderId",
                table: "Boleto",
                column: "OrderId",
                unique: true);
        }
    }
}
