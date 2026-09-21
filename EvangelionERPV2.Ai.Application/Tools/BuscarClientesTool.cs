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

        private readonly IRepository<Customer> _repository;

        public BuscarClientesTool(IRepository<Customer> repository) => _repository = repository;

        public string Name => "buscar_clientes";

        public string Description =>
            "Lista ou busca clientes. Use para descobrir o identificador de um cliente antes de " +
            "consultar pedidos ou contas a receber dele. Parâmetros: termo (parte do nome ou do " +
            "CPF/CNPJ — deixe vazio para listar todos) e pagina (1 é a primeira; use a próxima " +
            "quando a resposta vier truncada).";

        public string RequiredPermission => "customers.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string? termo = null, int? pagina = null) =>
            {
                var needle = (termo ?? string.Empty).Trim();
                var page = Math.Max(1, pagina ?? 1);

                var (customers, total) = await AiQuery.OrEmptyPageAsync(() => _repository.GetPagedAsync(
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && (needle == string.Empty
                             || x.Name.Contains(needle)
                             || (x.Document != null && x.Document.Contains(needle))),
                    query => query.OrderBy(x => x.Name),
                    page,
                    PageSize));

                var rows = customers.Select(x => new
                {
                    id = x.Id,
                    nome = x.Name,
                    telefone = x.PhoneNumber,
                    cidade = x.Municipio,
                    uf = x.Uf,
                    limiteCredito = x.CreditLimit,
                }).ToList();

                return AiJson.ForPage(rows, total, page, PageSize);
            }, Name, Description);
    }
}
