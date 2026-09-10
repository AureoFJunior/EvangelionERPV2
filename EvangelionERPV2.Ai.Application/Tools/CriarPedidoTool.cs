using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>One line of a proposed order, as the model supplies it.</summary>
    public sealed record ItemDoPedido(
        [property: Description("id do produto, o mesmo devolvido por buscar_produtos")] string produtoId,
        [property: Description("quantidade, maior que zero")] decimal quantidade);

    public sealed class CriarPedidoTool : IAiTool
    {
        private const int MaxItems = 50;

        private readonly IOrderService<Order> _service;
        private readonly IRepository<Product> _products;
        private readonly IRepository<Customer> _customers;

        public CriarPedidoTool(
            IOrderService<Order> service,
            IRepository<Product> products,
            IRepository<Customer> customers)
        {
            _service = service;
            _products = products;
            _customers = customers;
        }

        public string Name => "criar_pedido";

        public string Description =>
            "Lança um pedido para um cliente. Parâmetros: clienteId (o id devolvido por " +
            "buscar_clientes) e itens (lista de produtoId e quantidade). O pedido entra numa fila " +
            "e aparece na lista alguns instantes depois. Só acontece depois que a pessoa confirmar.";

        public string RequiredPermission => "orders.create";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments)
        {
            var items = AiArguments.Text(arguments, "itens");
            var lines = items.Length == 0 ? 0 : items.Count(character => character == '{');

            return $"Lançar um pedido para o cliente {AiArguments.Text(arguments, "clienteId")} "
                   + $"com {lines} item(ns). O pedido entra na fila e aparece na lista em seguida.";
        }

        public string? Validate(IReadOnlyDictionary<string, object?> arguments)
        {
            if (AiArguments.Id(arguments, "clienteId") is null)
                return "clienteId inválido: use o id completo devolvido por buscar_clientes, não o nome nem um pedaço do id.";

            var items = ReadItems(arguments);
            if (items.Count == 0)
                return "informe pelo menos um item, cada um com produtoId e quantidade.";

            if (items.Count > MaxItems)
                return $"no máximo {MaxItems} itens por pedido.";

            foreach (var (productId, quantity) in items)
            {
                if (productId is null)
                    return "produtoId inválido: use o id completo devolvido por buscar_produtos.";

                if (quantity is null or <= 0)
                    return "quantidade deve ser maior que zero.";
            }

            return null;
        }

        /// <summary>The itens array as the model sent it, without trusting its shape.</summary>
        private static List<(Guid? ProductId, decimal? Quantity)> ReadItems(IReadOnlyDictionary<string, object?> arguments)
        {
            var lines = new List<(Guid?, decimal?)>();

            if (!arguments.TryGetValue("itens", out var value) || value is null)
                return lines;

            try
            {
                using var document = JsonDocument.Parse(
                    value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value, AiJson.Options));

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return lines;

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        lines.Add((null, null));
                        continue;
                    }

                    var productId = item.TryGetProperty("produtoId", out var id)
                                    && Guid.TryParse(id.ValueKind == JsonValueKind.String ? id.GetString() : id.ToString(), out var parsed)
                                    && parsed != Guid.Empty
                        ? parsed
                        : (Guid?)null;

                    var quantity = item.TryGetProperty("quantidade", out var q) && q.TryGetDecimal(out var amount)
                        ? amount
                        : (decimal?)null;

                    lines.Add((productId, quantity));
                }
            }
            catch (JsonException)
            {
                lines.Add((null, null));
            }

            return lines;
        }

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string clienteId, ItemDoPedido[] itens) =>
            {
                if (!Guid.TryParse(clienteId, out var customerId) || customerId == Guid.Empty)
                    return "{\"erro\":\"clienteId inválido. Use buscar_clientes para obter o id.\"}";

                if (itens is null || itens.Length == 0)
                    return "{\"erro\":\"informe pelo menos um item\"}";

                if (itens.Length > MaxItems)
                    return $"{{\"erro\":\"no máximo {MaxItems} itens por pedido\"}}";

                var customer = await AiQuery.ByIdOrNullAsync(() => _customers.GetByIdAsync(customerId));
                if (customer is null || customer.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"cliente não encontrado nesta empresa\"}";

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = context.EnterpriseId,
                    CustomerId = customerId,
                    UserId = context.UserId,
                    Status = 0,
                };

                var lines = new List<OrderedProduct>();
                decimal total = 0;

                foreach (var item in itens)
                {
                    if (item.quantidade <= 0)
                        return "{\"erro\":\"quantidade deve ser maior que zero\"}";

                    if (!Guid.TryParse(item.produtoId, out var productId) || productId == Guid.Empty)
                        return "{\"erro\":\"produtoId inválido. Use buscar_produtos para obter o id.\"}";

                    var product = await AiQuery.ByIdOrNullAsync(() => _products.GetByIdAsync(productId));
                    if (product is null || product.EnterpriseId != context.EnterpriseId)
                        return "{\"erro\":\"produto não encontrado nesta empresa\"}";

                    total += product.DefaultValue * item.quantidade;

                    lines.Add(new OrderedProduct
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        ProductId = productId,
                        Quantity = item.quantidade,
                        Value = product.DefaultValue,
                        UnitOfMeasure = product.UnitOfMeasure,
                    });
                }

                order.TotalValue = total;
                order.OrderedProduct = lines;

                await _service.InsertOrderInQueue(order);

                // The queue, not the database: OrderService publishes and returns before the order
                // exists, so this says enqueued rather than created on purpose.
                return JsonSerializer.Serialize(
                    new { status = "na fila", pedidoId = order.Id, total, itens = lines.Count },
                    AiJson.Options);
            }, Name, Description);
    }
}
