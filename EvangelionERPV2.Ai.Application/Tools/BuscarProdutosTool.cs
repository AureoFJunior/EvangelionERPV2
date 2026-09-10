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
        private const int PageSize = AiJson.DefaultCap;

        // ponytail: fetch one row past the display cap so AiJson.ForModel can tell "exactly 20
        // matched" from "more than 20 matched" — with this, `total` is a lower bound (it reads 21
        // when 137 match), not an exact count. `truncado` is what the model actually needs and is
        // correct either way; an exact count would need a second COUNT query per search, which
        // isn't worth it for a chat answer.
        private const int FetchSize = PageSize + 1;

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

                if (needle.Length == 0)
                    return "{\"erro\":\"informe um termo de busca\"}";

                var products = await AiQuery.OrEmptyAsync(() => _repository.GetAllAsync(
                    1,
                    FetchSize,
                    // IsActive == true, not just IsActive: the column is bool? and there is no
                    // global soft-delete filter, so a deleted product is otherwise quoted at price.
                    x => x.EnterpriseId == context.EnterpriseId
                         && x.IsActive == true
                         && (x.Name.Contains(needle) || (x.Sku != null && x.Sku.Contains(needle)))));

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
