using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class AtualizarProdutoTool : IAiTool
    {
        private readonly IProductService<Product> _service;
        private readonly IRepository<Product> _repository;

        public AtualizarProdutoTool(IProductService<Product> service, IRepository<Product> repository)
        {
            _service = service;
            _repository = repository;
        }

        public string Name => "atualizar_produto";

        public string Description =>
            "Altera o preço de venda de um produto. Parâmetros: produtoId (o id devolvido por " +
            "buscar_produtos, ou simplesmente o nome do produto — o sistema resolve) e preco. " +
            "Não mexe em estoque: para isso use ajustar_estoque (correção de contagem) ou " +
            "receber_compra (chegada de mercadoria). Só acontece depois que a pessoa confirmar.";

        public string RequiredPermission => "products.update";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            "Alterar o produto " + AiArguments.Text(arguments, "produtoId")
            + AiArguments.Suffix(arguments, "preco", ": preço para R$ ");

        public async Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default)
        {
            var price = AiArguments.Number(arguments, "preco");

            if (price is null)
                return AiToolArguments.Refused("informe o preco.");

            if (price < 0)
                return AiToolArguments.Refused("preço não pode ser negativo.");

            var found = await AiEntityLookup.ProductAsync(
                _repository, AiArguments.Text(arguments, "produtoId"), context.EnterpriseId);

            if (found.Error is not null)
                return AiToolArguments.Refused(found.Error);

            return AiToolArguments.Ok(AiArguments.With(arguments, "produtoId", found.Id!.Value.ToString()));
        }

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string produtoId, decimal preco) =>
            {
                if (!Guid.TryParse(produtoId, out var productId) || productId == Guid.Empty)
                    return "{\"erro\":\"produtoId inválido. Use buscar_produtos para obter o id.\"}";

                if (preco < 0)
                    return "{\"erro\":\"preço não pode ser negativo\"}";

                var product = await AiQuery.ByIdOrNullAsync(() => _repository.GetByIdAsync(productId));
                if (product is null || product.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"produto não encontrado nesta empresa\"}";

                // Re-checked here, not just at propose time: a card can sit for minutes and the
                // product may have been deactivated in between.
                if (product.IsActive != true)
                    return "{\"erro\":\"este produto está inativo e não pode ser alterado\"}";

                // Stock is deliberately absent here. ProductService.ApplyStockMovementAsync is the
                // only place StorageQuantity may move, because the quantity, the ledger row saying
                // why, and the cache invalidation have to happen together. Assigning it here left
                // the scalar and the StockMovement ledger disagreeing.
                if (product.DefaultValue == preco)
                    return "{\"erro\":\"nada para alterar: o produto já está com esse preço\"}";

                product.DefaultValue = preco;

                var updated = await _service.UpdateAsync(product);

                return JsonSerializer.Serialize(
                    new { id = updated.Id, nome = updated.Name, preco = updated.DefaultValue },
                    AiJson.Options);
            }, Name, Description);
    }
}
