using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseCurrentBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CurrentBalance",
                table: "Enterprise",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.Sql(
                """
                UPDATE e
                SET e.CurrentBalance =
                    ISNULL((
                        SELECT SUM(o.TotalValue)
                        FROM [Order] o
                        WHERE o.EnterpriseId = e.Id
                          AND o.IsActive = 1
                          AND o.TotalValue > 0
                          AND o.RefundedAt IS NULL
                          AND o.Status <> 6
                          AND (
                                (o.Payday IS NOT NULL AND CAST(o.Payday AS date) <= CAST(GETUTCDATE() AS date))
                                OR
                                (o.Payday IS NULL
                                 AND o.Status IN (2, 3, 4, 5)
                                 AND CAST(o.PaymentScheduledDate AS date) <= CAST(GETUTCDATE() AS date))
                              )
                    ), 0)
                    -
                    ISNULL((
                        SELECT SUM(p.Amount)
                        FROM PayableBill p
                        WHERE p.EnterpriseId = e.Id
                          AND p.IsActive = 1
                          AND p.Amount > 0
                          AND p.RefundedAt IS NULL
                          AND p.IsPaid = 1
                          AND CAST(ISNULL(p.PaidAt, p.DueDate) AS date) <= CAST(GETUTCDATE() AS date)
                    ), 0)
                FROM Enterprise e;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentBalance",
                table: "Enterprise");
        }
    }
}
