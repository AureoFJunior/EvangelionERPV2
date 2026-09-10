using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ContasAPagarTool : IAiTool
    {
        private const int DefaultHorizonDays = 7;

        private readonly IRepository<PayableBill> _repository;

        public ContasAPagarTool(IRepository<PayableBill> repository) => _repository = repository;

        public string Name => "contas_a_pagar";

        public string Description =>
            "Contas a pagar em aberto, da mais atrasada para a mais próxima do vencimento. " +
            "Responde \"o que vence essa semana\", \"o que está atrasado\" e \"quanto tenho a pagar\". " +
            "Parâmetro: diasAFrente (janela de vencimento, padrão 7). O que já venceu entra sempre.";

        public string RequiredPermission => "payable-bills.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? diasAFrente = null) =>
            {
                var horizon = DateTime.UtcNow.Date.AddDays(Math.Clamp(diasAFrente ?? DefaultHorizonDays, 1, 365));

                var bills = await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && !x.IsPaid
                         && x.RefundedAt == null
                         // Overdue is never filtered out by the window: a bill that came due last
                         // month is the first thing someone asking this needs to see.
                         && x.DueDate <= horizon));

                var today = DateTime.UtcNow.Date;
                var rows = bills
                    .OrderBy(x => x.DueDate)
                    .Take(AiJson.DefaultCap)
                    .Select(x => new
                    {
                        id = x.Id,
                        descricao = x.Description,
                        valor = x.Amount,
                        vencimento = x.DueDate.ToString("yyyy-MM-dd"),
                        vencida = x.DueDate.Date < today,
                        diasEmAtraso = x.DueDate.Date < today ? (today - x.DueDate.Date).Days : 0,
                        fornecedor = x.Supplier?.Name,
                    }).ToList();

                return AiJson.ForModel(rows, bills.Count);
            }, Name, Description);
    }
}
