using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <summary>
    /// The order records how it was settled, and a till sale is Finished rather than Paid.
    /// </summary>
    /// <remarks>
    /// The backfill is hand-written, like `AddFiscalNatOp` before it. EF generates the column and
    /// nothing else, but a PDV order that already exists is exactly as finished as one rung up
    /// tomorrow, and its tenders have been sitting in PdvSalePayment all along. Leaving history at
    /// Paid with a null method would split every report on the date this migration happened to run.
    ///
    /// Scoped through PdvSale, so a backoffice order that happens to be Paid is untouched and keeps
    /// a null PaymentMethod — which is the honest answer for an order nobody tendered against.
    /// </remarks>
    public partial class AddOrderPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Order",
                type: "int",
                nullable: true);

            // Status: Paid = 2, Finished = 5. PdvSale.Status: Synced = 2.
            //
            // Refunded orders are left alone: their status already says what became of them, and
            // moving one to Finished would lose that.
            //
            // A sale with no payment rows cannot exist — SyncSaleAsync refuses one — but the CASE
            // answers NULL rather than Mixed (98) if it ever did. A corrupt row should read as "not
            // known", never as a split payment that never happened.
            //
            // Through EXEC on purpose, like AddPayrollDetailEnterprise before it: the idempotent
            // script puts the AddColumn above and this statement in one batch, and SQL Server
            // parses a batch whole — a direct UPDATE naming PaymentMethod fails with "Invalid
            // column name" before the ALTER has run.
            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE o
                    SET o.Status        = 5,
                        o.PaymentMethod = m.Method
                    FROM [Order] o
                    JOIN PdvSale s ON s.OrderId = o.Id AND s.Status = 2
                    CROSS APPLY (
                        SELECT CASE WHEN COUNT(p.Method) = 0          THEN NULL
                                    WHEN COUNT(DISTINCT p.Method) = 1 THEN MIN(p.Method)
                                    ELSE 98 END AS Method
                        FROM PdvSalePayment p
                        WHERE p.PdvSaleId = s.Id
                    ) m
                    WHERE o.Status = 2
                      AND o.RefundedAt IS NULL;');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the column goes. The status is not restored: which orders were Paid before the
            // backfill is not recoverable from the rows themselves, and inventing an answer would
            // be worse than leaving the corrected one in place.
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Order");
        }
    }
}
