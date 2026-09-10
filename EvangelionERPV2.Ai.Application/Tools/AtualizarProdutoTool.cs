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
            "Altera preço ou estoque de um produto. Parâmetros: produtoId (o id devolvido por " +
            "buscar_produtos), preco, estoque. A alteração só acontece depois que a pessoa " +
            "confirmar na tela.";

        public string RequiredPermission => "products.update";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            "Alterar o produto " + AiArguments.Text(arguments, "produtoId")
            + AiArguments.Suffix(arguments, "preco", ": preço para R$ ")
            + AiArguments.Suffix(arguments, "estoque", ", estoque para ");

        public string? Validate(IReadOnlyDictionary<string, object?> arguments)
        {
            if (AiArguments.Id(arguments, "produtoId") is null)
                return "produtoId inválido: use o id completo devolvido por buscar_produtos, não o nome nem um pedaço do id.";

            var price = AiArguments.Number(arguments, "preco");
            var stock = AiArguments.Number(arguments, "estoque");

            if (price is null && stock is null)
                return "informe preco ou estoque.";

            return price < 0 || stock < 0 ? "preço e estoque não podem ser negativos." : null;
        }

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string produtoId, decimal? preco = null, decimal? estoque = null) =>
            {
                if (!Guid.TryParse(produtoId, out var productId) || productId == Guid.Empty)
                    return "{\"erro\":\"produtoId inválido. Use buscar_produtos para obter o id.\"}";

                if (preco is null && estoque is null)
                    return "{\"erro\":\"informe preco ou estoque\"}";

                if (preco is < 0 || estoque is < 0)
                    return "{\"erro\":\"preço e estoque não podem ser negativos\"}";

                var product = await AiQuery.ByIdOrNullAsync(() => _repository.GetByIdAsync(productId));
                if (product is null || product.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"produto não encontrado nesta empresa\"}";

                var changed = false;

                if (preco is not null && product.DefaultValue != preco.Value)
                {
                    product.DefaultValue = preco.Value;
                    changed = true;
                }

                if (estoque is not null && product.StorageQuantity != estoque.Value)
                {
                    product.StorageQuantity = estoque.Value;
                    changed = true;
                }

                // See AtualizarClienteTool: an echoed value is not a change, and must not read as one.
                if (!changed)
                    return "{\"erro\":\"nada para alterar: o produto já está com esses valores\"}";

                var updated = await _service.UpdateAsync(product);

                return JsonSerializer.Serialize(
                    new { id = updated.Id, nome = updated.Name, preco = updated.DefaultValue, estoque = updated.StorageQuantity },
                    AiJson.Options);
            }, Name, Description);
    }
}
