using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class BuscarClientesTool : IAiTool
    {
        private const int PageSize = AiJson.DefaultCap;

        // ponytail: fetch one row past the display cap so AiJson.ForModel can tell "exactly 20
        // matched" from "more than 20 matched" — with this, `total` is a lower bound (it reads 21
        // when 137 match), not an exact count. `truncado` is what the model actually needs and is
        // correct either way; an exact count would need a second COUNT query per search, which
        // isn't worth it for a chat answer.
        private const int FetchSize = PageSize + 1;

        private readonly IRepository<Customer> _repository;

        public BuscarClientesTool(IRepository<Customer> repository) => _repository = repository;

        public string Name => "buscar_clientes";

        public string Description =>
            "Busca clientes pelo nome ou documento (CPF/CNPJ). Use para descobrir o identificador " +
            "de um cliente antes de consultar pedidos ou contas a receber dele. " +
            "Parâmetro: termo (texto parcial do nome ou documento).";

        public string RequiredPermission => "customers.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string termo) =>
            {
                var needle = (termo ?? string.Empty).Trim();

                if (needle.Length == 0)
                    return "{\"erro\":\"informe um termo de busca\"}";

                var customers = await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
                    1,
                    FetchSize,
                    // IsActive == true, not just IsActive: the column is bool? and there is no
                    // global soft-delete filter, so a deleted customer is otherwise listed as live.
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && (x.Name.Contains(needle) || (x.Document != null && x.Document.Contains(needle)))));

                // Document is deliberately not returned: the model needs the id to chain a call,
                // and a CPF in the conversation log is personal data this feature does not need.
                var rows = customers.Select(x => new
                {
                    id = x.Id,
                    nome = x.Name,
                    cidade = x.Municipio,
                    uf = x.Uf,
                    limiteCredito = x.CreditLimit,
                }).ToList();

                return AiJson.ForModel(rows, rows.Count);
            }, Name, Description);
    }
}
