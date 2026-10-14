using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using System.Net;
using System.Text;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    public class OrderReportGeneratorService : IOrderReportGeneratorService
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;

        public OrderReportGeneratorService(EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<string> GenerateMonthlyBillingReportAsync(Enterprise enterprise, IEnumerable<Order> orders)
        {
            ArgumentNullException.ThrowIfNull(enterprise);
            ArgumentNullException.ThrowIfNull(orders);

            var body = new StringBuilder();

            body.AppendLine("<!DOCTYPE html>");
            body.AppendLine("<html>");
            body.AppendLine("<head>");
            body.AppendLine("<meta charset='UTF-8'>");
            body.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            body.AppendLine(GetStyleSheet());
            body.AppendLine("</head>");
            body.AppendLine("<body>");
            body.AppendLine(GetHeaderSection(enterprise.Name));
            body.AppendLine(await GetTableSectionAsync(orders));
            body.AppendLine(GetClosingTags());
            body.AppendLine("</body>");
            body.AppendLine("</html>");

            return body.ToString();
        }

        public async Task<string> GenerateTopProductsByRevenueReportAsync(Enterprise enterprise, IEnumerable<Order> orders)
        {
            ArgumentNullException.ThrowIfNull(enterprise);
            ArgumentNullException.ThrowIfNull(orders);

            var orderLines = orders
                .SelectMany(x => x.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
                .Where(x => x.Quantity > 0 && x.Value >= 0)
                .ToList();

            if (!orderLines.Any())
                return string.Empty;

            var productNames = await ResolveProductNamesAsync(orderLines);

            var rankedProducts = orderLines
                .GroupBy(x => x.ProductId)
                .Select(group =>
                {
                    var quantity = group.Sum(x => x.Quantity);
                    var revenue = group.Sum(x => Math.Max(0, x.Value) * Math.Max(0, x.Quantity));
                    var unit = group.Select(x => x.UnitOfMeasure).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
                    var productName = productNames.TryGetValue(group.Key, out var resolvedName) && !string.IsNullOrWhiteSpace(resolvedName)
                        ? resolvedName
                        : "Unknown Product";

                    return new TopProductsByRevenueRow(
                        productName,
                        quantity,
                        revenue,
                        unit);
                })
                .OrderByDescending(x => x.Revenue)
                .ThenBy(x => x.ProductName)
                .ToList();

            if (!rankedProducts.Any())
                return string.Empty;

            var topRows = rankedProducts.Take(10).ToList();
            var totalQuantity = rankedProducts.Sum(x => x.Quantity);
            var totalRevenue = rankedProducts.Sum(x => x.Revenue);

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
            <h1 class='title'>Top Products by Revenue</h1>
            <p class='subtitle'>{WebUtility.HtmlEncode(enterprise.Name ?? string.Empty)}</p>
            <div class='meta-row'>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>
            <div class='summary'>
                <div class='summary-card'>
                    <div class='summary-label'>Products Ranked</div>
                    <div class='summary-value'>{rankedProducts.Count:N0}</div>
                </div>
                <div class='summary-card'>
                    <div class='summary-label'>Total Quantity</div>
                    <div class='summary-value'>{totalQuantity:N2}</div>
                </div>
                <div class='summary-card'>
                    <div class='summary-label'>Total Revenue</div>
                    <div class='summary-value'>{totalRevenue:C}</div>
                </div>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>#</th>
                        <th>Product</th>
                        <th>Quantity</th>
                        <th style='text-align:right'>Revenue</th>
                    </tr>
                </thead>
                <tbody>");

            for (var index = 0; index < topRows.Count; index++)
            {
                var row = topRows[index];
                body.AppendLine($@"
                    <tr>
                        <td>{index + 1}</td>
                        <td>{WebUtility.HtmlEncode(row.ProductName)}</td>
                        <td>{row.Quantity:N2} {WebUtility.HtmlEncode(row.UnitOfMeasure)}</td>
                        <td class='value-cell'>{row.Revenue:C}</td>
                    </tr>");
            }

            body.AppendLine($@"
                    <tr class='total-row'>
                        <td colspan='2'>TOTAL</td>
                        <td>{topRows.Sum(x => x.Quantity):N2}</td>
                        <td class='value-cell'>{topRows.Sum(x => x.Revenue):C}</td>
                    </tr>
                </tbody>
            </table>
        </div>");
            body.AppendLine("</body>");
            body.AppendLine("</html>");

            return body.ToString();
        }

        public Task<string> GenerateSalesByStatusReportAsync(Enterprise enterprise, IEnumerable<Order> orders)
        {
            ArgumentNullException.ThrowIfNull(enterprise);
            ArgumentNullException.ThrowIfNull(orders);

            var orderList = orders.ToList();
            if (!orderList.Any())
                return Task.FromResult(string.Empty);

            var statusRows = orderList
                .GroupBy(x => x.Status)
                .Select(group => new SalesByStatusRow(
                    GetOrderStatusLabel(group.Key),
                    group.Count(),
                    group.Sum(x => Math.Max(0, x.TotalValue))))
                .OrderByDescending(x => x.TotalValue)
                .ThenBy(x => x.StatusLabel)
                .ToList();

            if (!statusRows.Any())
                return Task.FromResult(string.Empty);

            var totalOrders = statusRows.Sum(x => x.Orders);
            var totalValue = statusRows.Sum(x => x.TotalValue);

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
            <h1 class='title'>Sales by Status</h1>
            <p class='subtitle'>{WebUtility.HtmlEncode(enterprise.Name ?? string.Empty)}</p>
            <div class='meta-row'>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>
            <div class='summary'>
                <div class='summary-card'>
                    <div class='summary-label'>Orders</div>
                    <div class='summary-value'>{totalOrders:N0}</div>
                </div>
                <div class='summary-card'>
                    <div class='summary-label'>Total Sales Value</div>
                    <div class='summary-value'>{totalValue:C}</div>
                </div>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>Status</th>
                        <th>Orders</th>
                        <th style='text-align:right'>Total Value</th>
                    </tr>
                </thead>
                <tbody>");

            foreach (var row in statusRows)
            {
                body.AppendLine($@"
                    <tr>
                        <td>{WebUtility.HtmlEncode(row.StatusLabel)}</td>
                        <td>{row.Orders:N0}</td>
                        <td class='value-cell'>{row.TotalValue:C}</td>
                    </tr>");
            }

            body.AppendLine($@"
                    <tr class='total-row'>
                        <td>TOTAL</td>
                        <td>{totalOrders:N0}</td>
                        <td class='value-cell'>{totalValue:C}</td>
                    </tr>
                </tbody>
            </table>
        </div>");
            body.AppendLine("</body>");
            body.AppendLine("</html>");

            return Task.FromResult(body.ToString());
        }

        private string GetStyleSheet()
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
        </style>";
        }

        private string GetHeaderSection(string enterpriseName)
        {
            var encodedEnterpriseName = WebUtility.HtmlEncode(enterpriseName ?? string.Empty);

            return $@"
        <div class='container'>
            <h1 class='title'>Monthly Billing Report</h1>
            <p class='subtitle'>{encodedEnterpriseName}</p>
            <div class='meta-row'>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>
            <table>
                <thead>
                    <tr>
                        <th>Product</th>
                        <th>Quantity</th>
                        <th style='text-align:right'>Value</th>
                    </tr>
                </thead>
                <tbody>";
        }

        private async Task<string> GetTableSectionAsync(IEnumerable<Order> orders)
        {
            var body = new StringBuilder();
            var (rows, totalQuantity, totalValue) = await GenerateTableRowsAsync(orders);

            body.Append(rows);
            body.AppendLine(GetTotalRow(totalQuantity, totalValue));

            return body.ToString();
        }

        private async Task<(string rows, double totalQuantity, double totalValue)> GenerateTableRowsAsync(IEnumerable<Order> orders)
        {
            var rows = new StringBuilder();
            double totalQuantity = 0;
            double totalValue = 0;

            foreach (var order in orders)
            {
                var orderedProducts = order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>();
                foreach (var orderedProduct in orderedProducts)
                {
                    var productName = orderedProduct.Product?.Name;

                    if (string.IsNullOrWhiteSpace(productName))
                    {
                        try
                        {
                            var product = await _productRepository.GetByIdAsync(orderedProduct.ProductId);
                            productName = product?.Name;
                        }
                        catch
                        {
                            productName = null;
                        }
                    }

                    rows.AppendLine(GetProductRow(productName ?? "Unknown Product", orderedProduct.Quantity, orderedProduct.Value, orderedProduct.UnitOfMeasure));
                }

                totalQuantity += orderedProducts.Sum(x => x.Quantity);
                totalValue += order.TotalValue;
            }

            return (rows.ToString(), totalQuantity, totalValue);
        }

        private string GetProductRow(string productName, double quantity, double value, string unitOfMeasure)
        {
            var encodedProductName = WebUtility.HtmlEncode(productName ?? "Unknown Product");
            var encodedUnitOfMeasure = WebUtility.HtmlEncode(unitOfMeasure ?? string.Empty);

            return $@"
                    <tr>
                        <td>{encodedProductName}</td>
                        <td>{quantity:N2} {encodedUnitOfMeasure}</td>
                        <td class='value-cell'>{value:C}</td>
                    </tr>";
        }

        private string GetTotalRow(double totalQuantity, double totalValue)
        {
            return $@"
                    <tr class='total-row'>
                        <td>TOTAL</td>
                        <td>{totalQuantity:N2} items</td>
                        <td class='value-cell'>{totalValue:C}</td>
                    </tr>";
        }

        private string GetClosingTags()
        {
            return @"
                </tbody>
            </table>
        </div>";
        }

        private async Task<Dictionary<Guid, string>> ResolveProductNamesAsync(IEnumerable<OrderedProduct> orderedProducts)
        {
            var namesByProductId = orderedProducts
                .Where(x => x.ProductId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Product?.Name))
                .GroupBy(x => x.ProductId)
                .ToDictionary(x => x.Key, x => x.Select(y => y.Product?.Name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)) ?? "Unknown Product");

            var missingProductIds = orderedProducts
                .Where(x => x.ProductId != Guid.Empty && !namesByProductId.ContainsKey(x.ProductId))
                .Select(x => x.ProductId)
                .Distinct()
                .ToList();

            if (!missingProductIds.Any())
                return namesByProductId;

            var products = await _productRepository.GetAllAsyncByFilter(
                false,
                null,
                null,
                x => x.IsActive == true && missingProductIds.Contains(x.Id));

            foreach (var product in products)
            {
                if (product.Id == Guid.Empty || string.IsNullOrWhiteSpace(product.Name))
                    continue;

                namesByProductId[product.Id] = product.Name;
            }

            return namesByProductId;
        }

        private static string GetOrderStatusLabel(int status)
        {
            return Enum.IsDefined(typeof(EnumOrderStatus), status)
                ? ((EnumOrderStatus)status).ToString()
                : $"Unknown ({status})";
        }

        private readonly record struct TopProductsByRevenueRow(string ProductName, double Quantity, double Revenue, string UnitOfMeasure);
        private readonly record struct SalesByStatusRow(string StatusLabel, int Orders, double TotalValue);
    }
}
