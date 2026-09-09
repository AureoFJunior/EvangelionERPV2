using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class BuscarClientesTool : IAiTool
    {
        private const int PageSize = 20;
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

                var customers = (await _repository.GetAllAsync(
                    1,
                    PageSize,
                    x => x.EnterpriseId == context.EnterpriseId
                         && (x.Name.Contains(needle) || (x.Document != null && x.Document.Contains(needle))))).ToList();

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
