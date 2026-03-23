using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using System.Net;
using System.Text;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public class ProductReportGeneratorService : IProductReportGeneratorService
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;

        public ProductReportGeneratorService(EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<string> GenerateStockReportAsync(Enterprise enterprise)
        {
            ArgumentNullException.ThrowIfNull(enterprise);

            var products = await _productRepository.GetAllAsyncByFilter(
                false,
                null,
                null,
                x => x.IsActive == true && x.EnterpriseId == enterprise.Id,
                x => x.Name);

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
            body.AppendLine(GetTableSection(products));
            body.AppendLine(GetClosingTags());
            body.AppendLine("</body>");
            body.AppendLine("</html>");

            return body.ToString();
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

            .status-legend {
                display: flex;
                gap: 10px;
                margin-bottom: 16px;
                flex-wrap: wrap;
            }

            .badge {
                display: inline-flex;
                align-items: center;
                gap: 8px;
                border-radius: 999px;
                padding: 6px 10px;
                font-size: 12px;
                font-weight: 600;
                border: 1px solid transparent;
            }

            .badge-ok {
                background: #ecfdf5;
                border-color: #a7f3d0;
                color: #065f46;
            }

            .badge-out {
                background: #fef2f2;
                border-color: #fecaca;
                color: #991b1b;
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

            .status-text-ok {
                color: #065f46;
                font-weight: 600;
            }

            .status-text-out {
                color: #991b1b;
                font-weight: 600;
            }

            .summary {
                display: flex;
                gap: 12px;
                flex-wrap: wrap;
                margin-top: 16px;
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
            <h1 class='title'>Stock Report</h1>
            <p class='subtitle'>{encodedEnterpriseName}</p>
            <div class='meta-row'>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>

            <div class='status-legend'>
                <span class='badge badge-ok'>Stock Available</span>
                <span class='badge badge-out'>Out of Stock</span>
            </div>

            <table>
                <thead>
                    <tr>
                        <th>Status</th>
                        <th>Product</th>
                        <th>Description</th>
                        <th>Quantity</th>
                    </tr>
                </thead>
                <tbody>";
        }

        private string GetTableSection(IEnumerable<Product> products)
        {
            var body = new StringBuilder();
            int okCount = 0;
            int outCount = 0;

            foreach (var product in products)
            {
                var status = GetStockStatus(product.StorageQuantity);
                body.AppendLine(GetProductRow(product, status));

                if (status == "ok") okCount++;
                else outCount++;
            }

            body.AppendLine("</tbody>");
            body.AppendLine("</table>");
            body.AppendLine(GetSummarySection(okCount, outCount));

            return body.ToString();
        }

        private string GetStockStatus(double storageQuantity)
        {
            return storageQuantity > 0 ? "ok" : "out";
        }

        private string GetProductRow(Product product, string status)
        {
            var statusLabel = status == "ok" ? "Available" : "Out";
            var statusClass = status == "ok" ? "status-text-ok" : "status-text-out";
            var productName = WebUtility.HtmlEncode(product.Name ?? "N/A");
            var productDescription = WebUtility.HtmlEncode(product.Description ?? "N/A");
            var unitOfMeasure = WebUtility.HtmlEncode(product.UnitOfMeasure ?? string.Empty);

            return $@"
                    <tr>
                        <td><span class='{statusClass}'>{statusLabel}</span></td>
                        <td>{productName}</td>
                        <td>{productDescription}</td>
                        <td>{product.StorageQuantity:N2} {unitOfMeasure}</td>
                    </tr>";
        }

        private string GetSummarySection(int okCount, int outCount)
        {
            return $@"
            <div class='summary'>
                <div class='summary-card'>
                    <div class='summary-label'>Available Products</div>
                    <div class='summary-value'>{okCount}</div>
                </div>
                <div class='summary-card'>
                    <div class='summary-label'>Out of Stock Products</div>
                    <div class='summary-value'>{outCount}</div>
                </div>
            </div>";
        }

        private string GetClosingTags()
        {
            return @"
        </div>";
        }
    }
}
