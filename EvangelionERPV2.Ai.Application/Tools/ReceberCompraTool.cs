using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>
    /// The arrival event for bought stock: it marks a ProductStock payable bill as received, which
    /// posts a Receipt movement per line. This is the only way the agent may add stock — a purchase
    /// that was paid for and then arrived, rather than a number someone typed.
    /// </summary>
    public sealed class ReceberCompraTool : IAiTool
    {
        private readonly IPayableBillService _service;
        private readonly IRepository<PayableBill> _repository;

        public ReceberCompraTool(IPayableBillService service, IRepository<PayableBill> repository)
        {
            _service = service;
            _repository = repository;
        }

        public string Name => "receber_compra";

        public string Description =>
            "Dá entrada no estoque da mercadoria de uma compra que chegou. Parâmetro: compraId (o id " +
            "da conta a pagar de compra de produtos, ou parte da descrição dela). Lança a entrada de " +
            "cada item no histórico de estoque. Use compras_pendentes para ver o que ainda não " +
            "chegou. Só acontece depois que a pessoa confirmar.";

        public string RequiredPermission => "payable-bills.products.receive";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            $"Dar entrada no estoque dos itens da compra {AiArguments.Text(arguments, "compraId")}";

        public async Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default)
        {
            var text = AiArguments.Text(arguments, "compraId");

            if (text.Length == 0)
                return AiToolArguments.Refused("informe a compra: o id ou parte da descrição.");

            if (Guid.TryParse(text, out var id) && id != Guid.Empty)
                return AiToolArguments.Ok(AiArguments.With(arguments, "compraId", id.ToString()));

            var candidates = (await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
                    bill => bill.EnterpriseId == context.EnterpriseId
                            && bill.IsActive == true
                            && bill.BillType == (int)EnumPayableBillType.ProductStock
                            && bill.ProductsReceivedAt == null
                            && bill.RefundedAt == null
                            && bill.Description.Contains(text))))
                .ToList();

            return candidates.Count switch
            {
                0 => AiToolArguments.Refused(
                    $"nenhuma compra pendente de recebimento corresponde a \"{text}\". "
                    + "Chame compras_pendentes para ver as que estão esperando."),
                1 => AiToolArguments.Ok(AiArguments.With(arguments, "compraId", candidates[0].Id.ToString())),
                _ => AiToolArguments.Refused(
                    "mais de uma compra corresponde: "
                    + string.Join("; ", candidates.Take(5).Select(bill => $"{bill.Description} ({bill.Id})"))
                    + ". Pergunte qual antes de continuar."),
            };
        }

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string compraId) =>
            {
                if (!Guid.TryParse(compraId, out var billId) || billId == Guid.Empty)
                    return "{\"erro\":\"compraId inválido. Use compras_pendentes para ver as compras.\"}";

                try
                {
                    var received = await _service.MarkProductsReceivedAsync(billId, context.EnterpriseId);

                    return JsonSerializer.Serialize(
                        new
                        {
                            id = received.Id,
                            descricao = received.Description,
                            recebidoEm = received.ProductsReceivedAt,
                            status = "entrada lançada no estoque",
                        },
                        AiJson.Options);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The service refuses a second receipt, a refunded bill and one with no items;
                    // all three are answers the person needs to read, not a generic failure.
                    return JsonSerializer.Serialize(new { erro = Explain(ex) }, AiJson.Options);
                }
            }, Name, Description);

        private static string Explain(Exception exception) => exception.Message switch
        {
            var message when message.Contains("already received", StringComparison.OrdinalIgnoreCase)
                => "esta compra já teve a entrada lançada",
            var message when message.Contains("no product items", StringComparison.OrdinalIgnoreCase)
                => "esta conta não tem itens de produto para dar entrada",
            var message when message.Contains("Refunded", StringComparison.OrdinalIgnoreCase)
                => "esta compra foi estornada",
            _ => "não foi possível dar entrada nesta compra",
        };
    }
}
