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
            "buscar_clientes, ou o nome) e itens (lista de produtoId — id ou nome — e quantidade). O pedido entra numa fila " +
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

        public async Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default)
        {
            var items = ReadItems(arguments);

            if (items.Count == 0)
                return AiToolArguments.Refused("informe pelo menos um item, cada um com produtoId e quantidade.");

            if (items.Count > MaxItems)
                return AiToolArguments.Refused($"no máximo {MaxItems} itens por pedido.");

            var customer = await AiEntityLookup.CustomerAsync(
                _customers, AiArguments.Text(arguments, "clienteId"), context.EnterpriseId);

            if (customer.Error is not null)
                return AiToolArguments.Refused(customer.Error);

            var resolved = new List<object>(items.Count);

            foreach (var (productText, quantity) in items)
            {
                if (quantity is null or <= 0)
                    return AiToolArguments.Refused("quantidade deve ser maior que zero.");

                var product = await AiEntityLookup.ProductAsync(_products, productText, context.EnterpriseId);
                if (product.Error is not null)
                    return AiToolArguments.Refused(product.Error);

                resolved.Add(new { produtoId = product.Id!.Value.ToString(), quantidade = quantity.Value });
            }

            var withCustomer = AiArguments.With(arguments, "clienteId", customer.Id!.Value.ToString());

            return AiToolArguments.Ok(
                AiArguments.With(withCustomer, "itens", JsonSerializer.SerializeToElement(resolved, AiJson.Options)));
        }

        /// <summary>The itens array as the model sent it, without trusting its shape.</summary>
        private static List<(string ProductText, decimal? Quantity)> ReadItems(IReadOnlyDictionary<string, object?> arguments)
        {
            var lines = new List<(string, decimal?)>();

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
                        lines.Add((string.Empty, null));
                        continue;
                    }

                    var productText = item.TryGetProperty("produtoId", out var id)
                        ? (id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : id.ToString())
                        : string.Empty;

                    var quantity = item.TryGetProperty("quantidade", out var q) && q.TryGetDecimal(out var amount)
                        ? amount
                        : (decimal?)null;

                    lines.Add((productText, quantity));
                }
            }
            catch (JsonException)
            {
                lines.Add((string.Empty, null));
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

                return JsonSerializer.Serialize(
                    new { status = "na fila", pedidoId = order.Id, total, itens = lines.Count },
                    AiJson.Options);
            }, Name, Description);
    }
}
