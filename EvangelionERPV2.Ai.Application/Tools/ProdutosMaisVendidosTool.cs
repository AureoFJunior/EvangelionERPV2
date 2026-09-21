using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ProdutosMaisVendidosTool : IAiTool
    {
        private const int DefaultDays = 90;
        private const int DefaultLimit = 10;

        private readonly IRepository<Order> _orders;

        public ProdutosMaisVendidosTool(IRepository<Order> orders) => _orders = orders;

        public string Name => "produtos_mais_vendidos";

        public string Description =>
            "Ranking dos produtos mais vendidos no período, por quantidade e por faturamento. " +
            "Responde \"qual o produto mais vendido\", \"o que mais sai\" e \"o que mais fatura\". " +
            "Parâmetros: diasAtras (padrão 90) e limite (padrão 10). Pedidos estornados não contam.";

        public string RequiredPermission => "products.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasAtras = null, int? limite = null) =>
            {
                var window = DateTime.UtcNow.AddDays(-Math.Clamp(diasAtras ?? DefaultDays, 1, 365));
                var take = Math.Clamp(limite ?? DefaultLimit, 1, AiJson.DefaultCap);

                // ponytail: the window's order lines are ranked in memory. Fine at this size, and the
                // 365-day clamp bounds it; if a tenant's history outgrows that, this becomes a
                // GROUP BY in an OrderRepository method.
                var orders = await AiQuery.OrEmptyAsync(() => _orders.GetAllAsync(
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && x.CreatedAt >= window));

                var lines = orders
                    .Where(order => !ReceivableResolver.IsOrderRefunded(order))
                    .SelectMany(order => order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
                    .Where(line => line.Quantity > 0)
                    .ToList();

                if (lines.Count == 0)
                    return AiJson.ForModel(Array.Empty<object>(), 0);

                var rows = lines
                    .GroupBy(line => line.ProductId)
                    .Select(group => new
                    {
                        id = group.Key,
                        nome = group.Select(line => line.Product?.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                               ?? "produto removido",
                        quantidade = group.Sum(line => line.Quantity),
                        faturamento = group.Sum(line => line.Quantity * line.Value),
                        pedidos = group.Select(line => line.OrderId).Distinct().Count(),
                    })
                    .OrderByDescending(row => row.quantidade)
                    .ThenByDescending(row => row.faturamento)
                    .Take(take)
                    .ToList();

                return AiJson.ForModel(rows, rows.Count);
            }, Name, Description);
    }
}
