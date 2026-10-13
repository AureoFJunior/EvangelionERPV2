using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ComprasPendentesTool : IAiTool
    {
        private readonly IRepository<PayableBill> _repository;

        public ComprasPendentesTool(IRepository<PayableBill> repository) => _repository = repository;

        public string Name => "compras_pendentes";

        public string Description =>
            "Compras de mercadoria já lançadas que ainda não deram entrada no estoque. Responde " +
            "\"o que comprei e não chegou\" e serve para achar a compra antes de usar receber_compra. " +
            "Sem parâmetros.";

        public string RequiredPermission => "payable-bills.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async () =>
            {
                var bills = await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
                    bill => bill.EnterpriseId == context.EnterpriseId
                            && bill.IsActive == true
                            && bill.BillType == (int)EnumPayableBillType.ProductStock
                            && bill.ProductsReceivedAt == null
                            && bill.RefundedAt == null));

                var rows = bills
                    .OrderBy(bill => bill.DueDate)
                    .Take(AiJson.DefaultCap)
                    .Select(bill => new
                    {
                        id = bill.Id,
                        descricao = bill.Description,
                        valor = bill.Amount,
                        vencimento = bill.DueDate.ToString("yyyy-MM-dd"),
                        pago = bill.IsPaid,
                    }).ToList();

                return AiJson.ForModel(rows, bills.Count);
            }, Name, Description);
    }
}
