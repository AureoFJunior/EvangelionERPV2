using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class BuscarProdutosTool : IAiTool
    {
        private const int PageSize = AiJson.DefaultCap;

        private readonly IRepository<Product> _repository;

        public BuscarProdutosTool(IRepository<Product> repository) => _repository = repository;

        public string Name => "buscar_produtos";

        public string Description =>
            "Lista ou busca produtos do catálogo. Use para o preço, o estoque ou o identificador de " +
            "um produto. Parâmetros: termo (parte do nome ou SKU — deixe vazio para listar tudo) e " +
            "pagina (1 é a primeira; use a próxima quando a resposta vier truncada).";

        public string RequiredPermission => "products.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string? termo = null, int? pagina = null) =>
            {
                // No termo lists the catalogue instead of refusing: "lista os produtos" is a question
                // people actually ask, and a search tool that demands a needle cannot answer it.
                var needle = (termo ?? string.Empty).Trim();
                var page = Math.Max(1, pagina ?? 1);

                var (products, total) = await AiQuery.OrEmptyPageAsync(() => _repository.GetPagedAsync(
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && (needle == string.Empty
                             || x.Name.Contains(needle)
                             || (x.Sku != null && x.Sku.Contains(needle))),
                    query => query.OrderBy(x => x.Name),
                    page,
                    PageSize));

                var rows = products.Select(x => new
                {
                    id = x.Id,
                    nome = x.Name,
                    sku = x.Sku,
                    preco = x.DefaultValue,
                    estoque = x.StorageQuantity,
                    unidade = x.UnitOfMeasure,
                }).ToList();

                return AiJson.ForPage(rows, total, page, PageSize);
            }, Name, Description);
    }
}
