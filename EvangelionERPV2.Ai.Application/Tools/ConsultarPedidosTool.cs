using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ConsultarPedidosTool : IAiTool
    {
        private readonly IRepository<Order> _repository;

        public ConsultarPedidosTool(IRepository<Order> repository) => _repository = repository;

        public string Name => "consultar_pedidos";

        public string Description =>
            "Pedidos do período, do mais recente para o mais antigo, com o total e o ticket médio " +
            "da janela. Responde \"quanto vendi hoje\", \"qual o ticket médio\", \"últimos pedidos\" " +
            "e \"pedidos de um cliente\". Parâmetros: diasAtras (janela em dias, padrão 30 — para " +
            "\"o pedido mais recente\" mantenha a janela larga, a lista já vem ordenada) e clienteId.";

        public string RequiredPermission => "orders.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasAtras = null, string? clienteId = null) =>
            {
                var window = DateTime.UtcNow.AddDays(-Math.Clamp(diasAtras ?? 30, 1, 365));

                var customerId = Guid.Empty;
                if (!string.IsNullOrWhiteSpace(clienteId)
                    && (!Guid.TryParse(clienteId, out customerId) || customerId == Guid.Empty))
                {
                    return "{\"erro\":\"clienteId inválido. Use buscar_clientes para obter o id.\"}";
                }

                var orders = await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
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
                        status = ((EnumOrderStatus)x.Status).ToString(),
                        cliente = x.Customer?.Name,
                    }).ToList();

                var sold = orders.Where(order => !ReceivableResolver.IsOrderRefunded(order)).ToList();
                var revenue = sold.Sum(order => order.TotalValue);

                return JsonSerializer.Serialize(new
                {
                    total = orders.Count,
                    mostrando = rows.Count,
                    truncado = orders.Count > rows.Count,
                    resumo = new
                    {
                        pedidos = sold.Count,
                        faturamento = revenue,
                        ticketMedio = sold.Count == 0 ? 0m : Math.Round(revenue / sold.Count, 2),
                    },
                    itens = rows,
                }, AiJson.Options);
            }, Name, Description);
    }
}
