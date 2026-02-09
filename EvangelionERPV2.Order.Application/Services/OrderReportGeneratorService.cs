using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
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

        private string GetStyleSheet()
        {
            return @"
        <style>
            @import url('https://fonts.googleapis.com/css2?family=Orbitron:wght@400;700;900&family=Rajdhani:wght@300;500;700&display=swap');
            
            * {
                margin: 0;
                padding: 0;
                box-sizing: border-box;
            }
            
            html, body {
                width: 100%;
                height: 100%;
            }
            
            body {
                background: linear-gradient(135deg, #0a0e27 0%, #1a1a2e 50%, #16213e 100%);
                font-family: 'Rajdhani', sans-serif;
                color: #00ffff;
                padding: 40px 20px;
                min-height: 100vh;
            }
            
            .container {
                max-width: 1200px;
                margin: 0 auto;
                background: rgba(10, 14, 39, 0.95);
                border: 2px solid #00ffff;
                border-radius: 20px;
                padding: 40px;
                box-shadow: 
                    0 0 30px rgba(0, 255, 255, 0.3),
                    inset 0 0 30px rgba(0, 255, 255, 0.1);
                position: relative;
                overflow: hidden;
            }
            
            .container::before {
                content: '';
                position: absolute;
                top: -2px;
                left: -2px;
                right: -2px;
                bottom: -2px;
                background: linear-gradient(45deg, #00ffff, #ff00ff, #00ffff);
                border-radius: 20px;
                z-index: -1;
                opacity: 0.3;
                filter: blur(10px);
            }
            
            .header-title {
                font-family: 'Orbitron', sans-serif;
                font-size: 3em;
                font-weight: 900;
                text-align: center;
                margin-bottom: 10px;
                text-transform: uppercase;
                color: #00ffff;
                background: linear-gradient(90deg, #00ffff, #ff00ff, #00ffff);
                background-size: 200% auto;
                -webkit-background-clip: text;
                -webkit-text-fill-color: transparent;
                background-clip: text;
                letter-spacing: 4px;
                text-shadow: 0 0 20px rgba(0, 255, 255, 0.5);
                animation: glow 2s ease-in-out infinite alternate;
            }
            
            @keyframes glow {
                from { filter: drop-shadow(0 0 10px #00ffff); }
                to { filter: drop-shadow(0 0 20px #ff00ff); }
            }
            
            .enterprise-name {
                font-family: 'Orbitron', sans-serif;
                text-align: center;
                font-size: 1.8em;
                margin-bottom: 40px;
                color: #ff00ff;
                text-transform: uppercase;
                letter-spacing: 3px;
            }
            
            .billing-table {
                width: 100%;
                border-collapse: separate;
                border-spacing: 0;
                margin-top: 30px;
                border-radius: 15px;
                overflow: hidden;
                box-shadow: 0 0 40px rgba(255, 0, 255, 0.2);
            }
            
            .billing-table thead {
                background: linear-gradient(135deg, #ff00ff 0%, #00ffff 100%);
            }
            
            .billing-table thead th {
                font-family: 'Orbitron', sans-serif;
                padding: 20px;
                text-align: left;
                color: #0a0e27;
                font-weight: 900;
                font-size: 1.1em;
                text-transform: uppercase;
                letter-spacing: 2px;
                border: none;
            }
            
            .billing-table tbody tr {
                background: rgba(22, 33, 62, 0.6);
                border-bottom: 1px solid rgba(0, 255, 255, 0.2);
                transition: all 0.3s ease;
            }
            
            .billing-table tbody tr:hover {
                background: rgba(0, 255, 255, 0.1);
                transform: translateX(5px);
                box-shadow: -5px 0 15px rgba(255, 0, 255, 0.3);
            }
            
            .billing-table tbody tr.total-row {
                background: linear-gradient(90deg, rgba(255, 0, 255, 0.3), rgba(0, 255, 255, 0.3));
                border-top: 2px solid #00ffff;
                font-weight: 700;
            }
            
            .billing-table tbody tr.total-row:hover {
                transform: none;
            }
            
            .billing-table td {
                padding: 18px 20px;
                color: #00ffff;
                font-size: 1.1em;
                border: none;
            }
            
            .billing-table .total-row td {
                font-family: 'Orbitron', sans-serif;
                font-size: 1.2em;
                color: #fff;
                text-shadow: 0 0 10px rgba(0, 255, 255, 0.8);
            }
            
            @keyframes scan {
                0% { transform: translateY(-100%); }
                100% { transform: translateY(100%); }
            }
            
            .scanline {
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 2px;
                background: rgba(0, 255, 255, 0.3);
                animation: scan 4s linear infinite;
                pointer-events: none;
            }
        </style>";
        }

        private string GetHeaderSection(string enterpriseName)
        {
            return $@"
        <div class='container'>
            <div class='scanline'></div>
            <h2 class='header-title'>Monthly Billing</h2>
            <h3 class='enterprise-name'>⟨ {enterpriseName} ⟩</h3>
            <table class='billing-table'>
                <thead>
                    <tr>
                        <th>▶ Product</th>
                        <th>▶ Quantity</th>
                        <th>▶ Value</th>
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
                    var product = await _productRepository.GetByIdAsync(orderedProduct.ProductId) ?? new Product();
                    rows.AppendLine(GetProductRow(product?.Name ?? "Unknown Product", orderedProduct.Quantity, orderedProduct.Value, orderedProduct.UnitOfMeasure));
                }

                totalQuantity += orderedProducts.Sum(x => x.Quantity);
                totalValue += order.TotalValue;
            }

            return (rows.ToString(), totalQuantity, totalValue);
        }

        private string GetProductRow(string productName, double quantity, double value, string unitOfMesure)
        {
            return $@"
                    <tr>
                        <td>{productName}</td>
                        <td>{quantity} - {unitOfMesure}</td>
                        <td>{value:C}</td>
                    </tr>";
        }

        private string GetTotalRow(double totalQuantity, double totalValue)
        {
            return $@"
                    <tr class='total-row'>
                        <td>◆ TOTAL</td>
                        <td>Items: {totalQuantity}</td>
                        <td>{totalValue:C}</td>
                    </tr>";
        }

        private string GetClosingTags()
        {
            return @"
                </tbody>
            </table>
        </div>";
        }
    }
}
