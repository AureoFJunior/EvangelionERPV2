using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ClientesInadimplentesTool : IAiTool
    {
        private readonly IReceivablesReportService _service;

        public ClientesInadimplentesTool(IReceivablesReportService service) => _service = service;

        public string Name => "clientes_inadimplentes";

        public string Description =>
            "Lista os clientes com parcelas vencidas: quanto devem, há quantos dias e quantas " +
            "parcelas. Responde \"quem está devendo\", \"quem está atrasado\" e \"quem está " +
            "inadimplente\". Parâmetro: diasDeAtraso (mínimo de dias em atraso, padrão 1).";

        public string RequiredPermission => "bills.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasDeAtraso = null) =>
            {
                var minimumDaysOverdue = Math.Clamp(diasDeAtraso ?? 1, 1, 3650);

                try
                {
                    var delinquent = await _service.GetDelinquentAsync(context.EnterpriseId, minimumDaysOverdue);

                    var rows = delinquent.Select(customer => new
                    {
                        id = customer.CustomerId,
                        nome = customer.CustomerName,
                        telefone = customer.PhoneNumber,
                        parcelasVencidas = customer.OverdueCount,
                        valorVencido = customer.OverdueAmount,
                        diasEmAtraso = customer.DaysOverdue,
                        vencimentoMaisAntigo = customer.OldestDueDate.ToString("yyyy-MM-dd"),
                    }).ToList();

                    return AiJson.ForModel(rows, rows.Count);
                }
                catch (NotFoundDatabaseException)
                {
                    return AiJson.ForModel(Array.Empty<object>(), 0);
                }
            }, Name, Description);
    }
}
