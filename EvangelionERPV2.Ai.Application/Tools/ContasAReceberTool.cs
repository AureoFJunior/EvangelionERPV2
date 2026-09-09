using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ContasAReceberTool : IAiTool
    {
        private readonly ICustomerSummaryService _service;

        public ContasAReceberTool(ICustomerSummaryService service) => _service = service;

        public string Name => "contas_a_receber";

        public string Description =>
            "Ficha financeira de um cliente: o que ele deve, o que está vencido e o histórico de " +
            "pagamento. Parâmetro: clienteId (o id devolvido por buscar_clientes). " +
            "Chame buscar_clientes antes se você só tiver o nome.";

        public string RequiredPermission => "bills.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string clienteId) =>
            {
                if (!Guid.TryParse(clienteId, out var customerId) || customerId == Guid.Empty)
                {
                    // Returned to the model as a tool result, not thrown. A bad argument is a thing
                    // the model can correct on the next iteration; an exception is not.
                    return "{\"erro\":\"clienteId inválido. Use buscar_clientes para obter o id.\"}";
                }

                try
                {
                    // includeFinancial: true is safe here only because the registry already checked
                    // bills.read before this tool was put in front of the model.
                    var summary = await _service.GetSummaryAsync(customerId, context.EnterpriseId, includeFinancial: true);

                    return summary is null
                        ? "{\"erro\":\"cliente não encontrado nesta empresa\"}"
                        : JsonSerializer.Serialize(summary);
                }
                catch (NotFoundDatabaseException)
                {
                    return "{\"erro\":\"cliente não encontrado nesta empresa\"}";
                }
            }, Name, Description);
    }
}
