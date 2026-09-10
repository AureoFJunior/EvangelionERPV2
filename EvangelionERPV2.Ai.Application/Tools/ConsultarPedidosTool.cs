using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>
    /// Recent orders for the tenant, optionally narrowed to one customer. Goes through
    /// IRepository&lt;Order&gt; rather than IOrderService so the date window and customer id are
    /// pushed into the SQL predicate: OrderRepository's paged read orders by Id, not by date, so
    /// paging first and filtering by CreatedAt in memory (the original approach) could silently
    /// drop real matches for a tenant with more than one page of orders.
    /// </summary>
    public sealed class ConsultarPedidosTool : IAiTool
    {
        private readonly IRepository<Order> _repository;

        public ConsultarPedidosTool(IRepository<Order> repository) => _repository = repository;

        public string Name => "consultar_pedidos";

        public string Description =>
            "Lista os pedidos da empresa em um período. Parâmetros: diasAtras (quantos dias para " +
            "trás olhar, padrão 30) e clienteId (opcional, o id devolvido por buscar_clientes). " +
            "Devolve valor, status e data de cada pedido.";

        public string RequiredPermission => "orders.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasAtras = null, string? clienteId = null) =>
            {
                var window = DateTime.UtcNow.AddDays(-Math.Clamp(diasAtras ?? 30, 1, 365));

                // Absent means "todos os clientes". Present but unparseable means the model passed a
                // name; dropping it silently would answer with every order in the window and let the
                // model report them as that customer's. Same contract as contas_a_receber.
                var customerId = Guid.Empty;
                if (!string.IsNullOrWhiteSpace(clienteId)
                    && (!Guid.TryParse(clienteId, out customerId) || customerId == Guid.Empty))
                {
                    return "{\"erro\":\"clienteId inválido. Use buscar_clientes para obter o id.\"}";
                }

                // ponytail: the date window bounds what gets loaded, so a very high-volume tenant
                // combined with a wide diasAtras would load more than it needs. Upgrade path is a
                // date-ordered, paged repository read once that pattern exists on Order.
                var orders = await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
                    // IsActive == true, not just IsActive: the column is bool? and there is no
                    // global soft-delete filter, so a soft-deleted order is otherwise counted as
                    // real. Order cancellation is a separate Status value, read below.
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && x.CreatedAt >= window
                         && (customerId == Guid.Empty || x.CustomerId == customerId)));

                var rows = orders
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(AiJson.DefaultCap)
                    .Select(x => new
                    {
                        id = x.Id,
                        data = x.CreatedAt.ToString("yyyy-MM-dd"),
                        valor = x.TotalValue,
                        // Order.Status is an int column, not an enum property. Cast it, or the
                        // model is handed "1" and will invent a meaning for it.
                        status = ((EnumOrderStatus)x.Status).ToString(),
                    }).ToList();

                return AiJson.ForModel(rows, orders.Count);
            }, Name, Description);
    }
}
