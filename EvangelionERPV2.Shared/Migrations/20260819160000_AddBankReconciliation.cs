using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Storage for imported bank statements. Purely additive: no existing table is touched, and in
    // particular Enterprise.CurrentBalance is left exactly as it was. The statement is a second
    // opinion the ERP balance can be checked against, never the balance itself.
    //
    // The [Migration] attribute is required for EF to discover this class at Database.Migrate()
    // time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260819160000_AddBankReconciliation")]
    public partial class AddBankReconciliation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAsOf = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    NewLineCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FitId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TrnType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Memo = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MatchedReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLine", x => x.Id);

                    // Restrict, not Cascade: the import row holds the bank's closing balance, and
                    // dropping it would leave its lines with nothing to reconcile against.
                    table.ForeignKey(
                        name: "FK_BankStatementLine_BankStatementImport_ImportId",
                        column: x => x.ImportId,
                        principalTable: "BankStatementImport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_EnterpriseId_BalanceAsOf",
                table: "BankStatementImport",
                columns: ["EnterpriseId", "BalanceAsOf"]);

            // EF's own foreign-key index.
            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_ImportId",
                table: "BankStatementLine",
                column: "ImportId");

            // The idempotency key, enforced by the database rather than by the import code alone:
            // FITID is the bank's own transaction id and is unique per account, so re-importing a
            // statement cannot double-count. Deliberately NOT filtered on IsActive — a soft-deleted
            // line still owns its FITID, and letting a replacement row in behind it would reinsert
            // the transaction this constraint exists to keep out.
            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_EnterpriseId_BankId_AccountId_FitId",
                table: "BankStatementLine",
                columns: ["EnterpriseId", "BankId", "AccountId", "FitId"],
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BankStatementLine");
            migrationBuilder.DropTable(name: "BankStatementImport");
        }
    }
}
