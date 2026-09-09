using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>
    /// Recent orders for the tenant, optionally narrowed to one customer. Date filtering happens
    /// here rather than in the service because the service has no date-ranged read and adding one
    /// for this feature alone would widen a shared interface for a single caller.
    /// </summary>
    public sealed class ConsultarPedidosTool : IAiTool
    {
        private const int FetchSize = 200;
        private readonly IOrderService<Order> _orderService;

        public ConsultarPedidosTool(IOrderService<Order> orderService) => _orderService = orderService;

        public string Name => "consultar_pedidos";

        public string Description =>
            "Lista os pedidos da empresa em um período. Parâmetros: diasAtras (quantos dias para " +
            "trás olhar, padrão 30) e clienteId (opcional, o id devolvido por buscar_clientes). " +
            "Devolve valor, status e data de cada pedido.";

        public string RequiredPermission => "orders.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasAtras, string? clienteId) =>
            {
                var window = DateTime.UtcNow.AddDays(-Math.Clamp(diasAtras ?? 30, 1, 365));
                Guid.TryParse(clienteId, out var customerId);

                var orders = (await _orderService.GetByEnterpriseIdAsync(context.EnterpriseId, 1, FetchSize))
                    .Where(x => x.CreatedAt >= window)
                    .Where(x => customerId == Guid.Empty || x.CustomerId == customerId)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();

                var rows = orders.Select(x => new
                {
                    id = x.Id,
                    data = x.CreatedAt.ToString("yyyy-MM-dd"),
                    valor = x.TotalValue,
                    // Order.Status is an int column, not an enum property. Cast it, or the
                    // model is handed "1" and will invent a meaning for it.
                    status = ((EnumOrderStatus)x.Status).ToString(),
                }).ToList();

                return AiJson.ForModel(rows, rows.Count);
            }, Name, Description);
    }
}
