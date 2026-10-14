using System.Net;
using System.Text;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class PayableBillReportGeneratorService : IPayableBillReportGeneratorService
    {
        private readonly IRepository<PayableBill> _payableBillRepository;

        public PayableBillReportGeneratorService(IRepository<PayableBill> payableBillRepository)
        {
            _payableBillRepository = payableBillRepository;
        }

        public async Task<string> GeneratePayablesOverviewReportAsync(Enterprise enterprise)
        {
            ArgumentNullException.ThrowIfNull(enterprise);

            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var today = now.Date;

            var bills = (await _payableBillRepository.GetAllAsyncByFilter(
                    false,
                    null,
                    null,
                    x => x.IsActive == true
                         && x.EnterpriseId == enterprise.Id
                         && x.DueDate >= monthStart
                         && x.DueDate <= monthEnd))
                .ToList();

            if (!bills.Any())
                return string.Empty;

            var groupedRows = bills
                .GroupBy(x => ResolveBillTypeLabel(x.BillType))
                .Select(group =>
                {
                    var paid = group.Where(x => !x.RefundedAt.HasValue && (x.IsPaid || x.PaidAt.HasValue)).Sum(x => Math.Max(0, x.Amount));
                    var overdue = group.Where(x => !x.RefundedAt.HasValue && !x.IsPaid && !x.PaidAt.HasValue && x.DueDate.Date < today).Sum(x => Math.Max(0, x.Amount));
                    var upcoming = group.Where(x => !x.RefundedAt.HasValue && !x.IsPaid && !x.PaidAt.HasValue && x.DueDate.Date >= today).Sum(x => Math.Max(0, x.Amount));
                    var refunded = group.Where(x => x.RefundedAt.HasValue).Sum(x => Math.Max(0, x.Amount));

                    return new PayablesOverviewRow(
                        group.Key,
                        paid,
                        overdue,
                        upcoming,
                        refunded);
                })
                .OrderByDescending(x => x.Total)
                .ThenBy(x => x.BillTypeLabel)
                .ToList();

            if (!groupedRows.Any())
                return string.Empty;

            var totalPaid = groupedRows.Sum(x => x.PaidAmount);
            var totalOverdue = groupedRows.Sum(x => x.OverdueAmount);
            var totalUpcoming = groupedRows.Sum(x => x.UpcomingAmount);
            var totalRefunded = groupedRows.Sum(x => x.RefundedAmount);
            var grandTotal = groupedRows.Sum(x => x.Total);

            var body = new StringBuilder();
            body.AppendLine("<!DOCTYPE html>");
            body.AppendLine("<html>");
            body.AppendLine("<head>");
            body.AppendLine("<meta charset='UTF-8'>");
            body.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            body.AppendLine(GetStyleSheet());
            body.AppendLine("</head>");
            body.AppendLine("<body>");
            body.AppendLine($@"
        <div class='container'>
            <h1 class='title'>Payables Overview</h1>
            <p class='subtitle'>{WebUtility.HtmlEncode(enterprise.Name ?? string.Empty)}</p>
            <div class='meta-row'>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>
            <div class='summary'>
                <div class='summary-card'>
                    <div class='summary-label'>Bill Types</div>
                    <div class='summary-value'>{groupedRows.Count:N0}</div>
                </div>
                <div class='summary-card'>
                    <div class='summary-label'>Overdue Amount</div>
                    <div class='summary-value'>{totalOverdue:C}</div>
                </div>
                <div class='summary-card'>
                    <div class='summary-label'>Current Month Total</div>
                    <div class='summary-value'>{grandTotal:C}</div>
                </div>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>Payable Type</th>
                        <th style='text-align:right'>Paid</th>
                        <th style='text-align:right'>Overdue</th>
                        <th style='text-align:right'>Upcoming</th>
                        <th style='text-align:right'>Refunded</th>
                        <th style='text-align:right'>Total</th>
                    </tr>
                </thead>
                <tbody>");

            foreach (var row in groupedRows)
            {
                body.AppendLine($@"
                    <tr>
                        <td>{WebUtility.HtmlEncode(row.BillTypeLabel)}</td>
                        <td class='value-cell'>{row.PaidAmount:C}</td>
                        <td class='value-cell'>{row.OverdueAmount:C}</td>
                        <td class='value-cell'>{row.UpcomingAmount:C}</td>
                        <td class='value-cell'>{row.RefundedAmount:C}</td>
                        <td class='value-cell'>{row.Total:C}</td>
                    </tr>");
            }

            body.AppendLine($@"
                    <tr class='total-row'>
                        <td>TOTAL</td>
                        <td class='value-cell'>{totalPaid:C}</td>
                        <td class='value-cell'>{totalOverdue:C}</td>
                        <td class='value-cell'>{totalUpcoming:C}</td>
                        <td class='value-cell'>{totalRefunded:C}</td>
                        <td class='value-cell'>{grandTotal:C}</td>
                    </tr>
                </tbody>
            </table>
        </div>");
            body.AppendLine("</body>");
            body.AppendLine("</html>");

            return body.ToString();
        }

        private static string ResolveBillTypeLabel(int billType)
        {
            return Enum.IsDefined(typeof(EnumPayableBillType), billType)
                ? ((EnumPayableBillType)billType).ToString()
                : EnumPayableBillType.Other.ToString();
        }

        private static string GetStyleSheet()
        {
            return @"
        <style>
            * {
                box-sizing: border-box;
            }

            html, body {
                margin: 0;
                padding: 0;
            }

            body {
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif;
                background: #f3f4f6;
                color: #111827;
                padding: 24px;
            }

            .container {
                max-width: 1100px;
                margin: 0 auto;
                background: #ffffff;
                border: 1px solid #e5e7eb;
                border-radius: 12px;
                padding: 24px;
            }

            .title {
                margin: 0 0 8px;
                font-size: 28px;
                line-height: 1.2;
                color: #111827;
            }

            .subtitle {
                margin: 0 0 18px;
                color: #4b5563;
                font-size: 14px;
            }

            .meta-row {
                margin-bottom: 20px;
                font-size: 12px;
                color: #6b7280;
            }

            .summary {
                display: flex;
                gap: 12px;
                flex-wrap: wrap;
                margin-bottom: 16px;
            }

            .summary-card {
                border: 1px solid #e5e7eb;
                border-radius: 10px;
                padding: 12px;
                min-width: 170px;
                background: #ffffff;
            }

            .summary-label {
                font-size: 12px;
                color: #6b7280;
                margin-bottom: 4px;
            }

            .summary-value {
                font-size: 20px;
                font-weight: 700;
                color: #111827;
            }

            table {
                width: 100%;
                border-collapse: collapse;
                font-size: 14px;
            }

            thead th {
                text-align: left;
                background: #1f2937;
                color: #ffffff;
                padding: 10px;
            }

            tbody td {
                border-bottom: 1px solid #e5e7eb;
                padding: 10px;
            }

            tbody tr:nth-child(even) {
                background: #f9fafb;
            }

            .value-cell {
                text-align: right;
                font-variant-numeric: tabular-nums;
            }

            .total-row td {
                font-weight: 700;
                background: #eef2ff;
                border-top: 2px solid #c7d2fe;
            }
        </style>";
        }

        private readonly record struct PayablesOverviewRow(
            string BillTypeLabel,
            double PaidAmount,
            double OverdueAmount,
            double UpcomingAmount,
            double RefundedAmount)
        {
            public double Total => PaidAmount + OverdueAmount + UpcomingAmount + RefundedAmount;
        }
    }
}
