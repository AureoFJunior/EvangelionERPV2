using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>
    /// Catalog lookup by name or SKU. Goes through IRepository, so the AppDbContext global query
    /// filter scopes it to the caller's tenant without this class knowing that happened.
    /// </summary>
    public sealed class BuscarProdutosTool : IAiTool
    {
        private const int PageSize = 20;
        private readonly IRepository<Product> _repository;

        public BuscarProdutosTool(IRepository<Product> repository) => _repository = repository;

        public string Name => "buscar_produtos";

        public string Description =>
            "Busca produtos do catálogo pelo nome ou SKU. Use quando precisar do preço, do estoque " +
            "ou do identificador de um produto. Parâmetro: termo (texto parcial do nome ou SKU).";

        public string RequiredPermission => "products.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string termo) =>
            {
                var needle = (termo ?? string.Empty).Trim();

                var products = (await _repository.GetAllAsync(
                    1,
                    PageSize,
                    x => x.EnterpriseId == context.EnterpriseId
                         && (x.Name.Contains(needle) || (x.Sku != null && x.Sku.Contains(needle))))).ToList();

                var rows = products.Select(x => new
                {
                    id = x.Id,
                    nome = x.Name,
                    sku = x.Sku,
                    preco = x.DefaultValue,
                    estoque = x.StorageQuantity,
                    unidade = x.UnitOfMeasure,
                }).ToList();

                return AiJson.ForModel(rows, rows.Count);
            }, Name, Description);
    }
}
