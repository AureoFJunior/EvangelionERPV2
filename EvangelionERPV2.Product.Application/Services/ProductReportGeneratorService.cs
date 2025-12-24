using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using System.Text;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public class ProductReportGeneratorService : IProductReportGeneratorService
    {
        private readonly IRepository<Product> _productRepository;

        public ProductReportGeneratorService(IRepository<Product> productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<string> GenerateStockReportAsync(Enterprise enterprise)
        {
            var products = await _productRepository.GetAllAsync();
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

            .legend {
                display: flex;
                justify-content: center;
                gap: 30px;
                margin-bottom: 30px;
                flex-wrap: wrap;
            }

            .legend-item {
                display: flex;
                align-items: center;
                gap: 10px;
                font-family: 'Orbitron', sans-serif;
                font-size: 0.9em;
            }

            .legend-indicator {
                width: 20px;
                height: 20px;
                border-radius: 50%;
                box-shadow: 0 0 10px currentColor;
            }

            .legend-indicator.ok {
                background: #00ff00;
                box-shadow: 0 0 15px #00ff00;
            }

            .legend-indicator.out {
                background: #ff0000;
                box-shadow: 0 0 15px #ff0000;
            }

            .legend-item {
                display: flex;
                align-items: center;
                gap: 10px;
                font-family: 'Orbitron', sans-serif;
                font-size: 1.1em;
                color: #ffffff;
                font-weight: 500;
            }
            
            .stock-table {
                width: 100%;
                border-collapse: separate;
                border-spacing: 0;
                margin-top: 30px;
                border-radius: 15px;
                overflow: hidden;
                box-shadow: 0 0 40px rgba(255, 0, 255, 0.2);
            }
            
            .stock-table thead {
                background: linear-gradient(135deg, #ff00ff 0%, #00ffff 100%);
            }
            
            .stock-table thead th {
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
            
            .stock-table tbody tr {
                background: rgba(22, 33, 62, 0.6);
                border-bottom: 1px solid rgba(0, 255, 255, 0.2);
                transition: all 0.3s ease;
            }
            
            .stock-table tbody tr:hover {
                background: rgba(0, 255, 255, 0.1);
                transform: translateX(5px);
                box-shadow: -5px 0 15px rgba(255, 0, 255, 0.3);
            }
            
            .stock-table td {
                padding: 18px 20px;
                color: #00ffff;
                font-size: 1.1em;
                border: none;
            }

            .stock-indicator {
                display: inline-flex;
                align-items: center;
                gap: 10px;
                font-weight: 700;
            }

            .status-dot {
                width: 15px;
                height: 15px;
                border-radius: 50%;
                animation: pulse 2s ease-in-out infinite;
            }

            .status-ok {
                background: #00ff00;
                box-shadow: 0 0 15px #00ff00;
            }

            .status-out {
                background: #ff0000;
                box-shadow: 0 0 15px #ff0000;
            }

            @keyframes pulse {
                0%, 100% { opacity: 1; transform: scale(1); }
                50% { opacity: 0.6; transform: scale(0.9); }
            }

            .quantity-ok { color: #00ff00; text-shadow: 0 0 10px #00ff00; }
            .quantity-out { color: #ff0000; text-shadow: 0 0 10px #ff0000; }
            
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

            .summary {
                display: flex;
                justify-content: space-around;
                margin-top: 30px;
                gap: 20px;
                flex-wrap: wrap;
            }

            .summary-card {
                background: rgba(22, 33, 62, 0.8);
                border: 2px solid;
                border-radius: 15px;
                padding: 20px 30px;
                text-align: center;
                min-width: 180px;
                box-shadow: 0 0 20px currentColor;
            }

            .summary-card.ok { border-color: #00ff00; }
            .summary-card.out { border-color: #ff0000; }

            .summary-label {
                font-family: 'Orbitron', sans-serif;
                font-size: 0.9em;
                margin-bottom: 10px;
                text-transform: uppercase;
                letter-spacing: 2px;
            }

            .summary-value {
                font-family: 'Orbitron', sans-serif;
                font-size: 2.5em;
                font-weight: 900;
            }
        </style>";
        }

        private string GetHeaderSection(string enterpriseName)
        {
            return $@"
        <div class='container'>
            <div class='scanline'></div>
            <h2 class='header-title'>Stock Report</h2>
            <h3 class='enterprise-name'>⟨ {enterpriseName} ⟩</h3>
            
            <div class='legend'>
                <div class='legend-item'>
                    <div class='legend-indicator ok'></div>
                    <span class='legend-item'>Stock Available</span>
                </div>
                <div class='legend-item'>
                    <div class='legend-indicator out'></div>
                    <span class='legend-item'>Out of Stock</span>
                </div>
            </div>

            <table class='stock-table'>
                <thead>
                    <tr>
                        <th>▶ Status</th>
                        <th>▶ Product</th>
                        <th>▶ Description</th>
                        <th>▶ Quantity</th>
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
            var statusLabel = status == "ok" ? "OK" : "OUT";

            return $@"
                    <tr>
                        <td>
                            <div class='stock-indicator'>
                                <div class='status-dot status-{status}'></div>
                                <span>{statusLabel}</span>
                            </div>
                        </td>
                        <td>{product.Name}</td>
                        <td>{product.Description ?? "N/A"}</td>
                        <td class='quantity-{status}'>{product.StorageQuantity:N2} {product.UnitOfMeasure}</td>
                    </tr>";
        }

        private string GetSummarySection(int okCount, int outCount)
        {
            return $@"
            <div class='summary'>
                <div class='summary-card ok'>
                    <div class='summary-label quantity-ok'>Available</div>
                    <div class='summary-value quantity-ok'>{okCount}</div>
                </div>
                <div class='summary-card out'>
                    <div class='summary-label quantity-out'>Out of Stock</div>
                    <div class='summary-value quantity-out'>{outCount}</div>
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