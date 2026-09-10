using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class MelhoresClientesTool : IAiTool
    {
        private const int DefaultDays = 90;
        private const int DefaultLimit = 10;

        private readonly IRepository<Order> _orders;

        public MelhoresClientesTool(IRepository<Order> orders) => _orders = orders;

        public string Name => "melhores_clientes";

        public string Description =>
            "Ranking dos clientes que mais compraram no período, por valor e por número de pedidos. " +
            "Responde \"qual cliente mais comprou\", \"meus melhores clientes\" e \"quem compra mais\". " +
            "Parâmetros: diasAtras (padrão 90) e limite (padrão 10). Pedidos estornados não contam.";

        public string RequiredPermission => "customers.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasAtras = null, int? limite = null) =>
            {
                var window = DateTime.UtcNow.AddDays(-Math.Clamp(diasAtras ?? DefaultDays, 1, 365));
                var take = Math.Clamp(limite ?? DefaultLimit, 1, AiJson.DefaultCap);

                // ponytail: same shape and same ceiling as produtos_mais_vendidos — the window's
                // orders are ranked in memory, bounded by the 365-day clamp.
                var orders = await AiQuery.OrEmptyAsync(() => _orders.GetAllAsync(
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && x.CreatedAt >= window
                         && x.CustomerId != null));

                var rows = orders
                    .Where(order => !ReceivableResolver.IsOrderRefunded(order))
                    .GroupBy(order => order.CustomerId!.Value)
                    .Select(group => new
                    {
                        id = group.Key,
                        // OrderRepository includes Customer, so the name comes with the order.
                        nome = group.Select(order => order.Customer?.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                               ?? "cliente removido",
                        totalComprado = group.Sum(order => order.TotalValue),
                        pedidos = group.Count(),
                        ultimaCompra = group.Max(order => order.CreatedAt).ToString("yyyy-MM-dd"),
                    })
                    .OrderByDescending(row => row.totalComprado)
                    .ThenByDescending(row => row.pedidos)
                    .Take(take)
                    .ToList();

                return AiJson.ForModel(rows, rows.Count);
            }, Name, Description);
    }
}
