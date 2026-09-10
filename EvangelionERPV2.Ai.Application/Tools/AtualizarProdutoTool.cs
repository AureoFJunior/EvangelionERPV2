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

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string produtoId, decimal? preco = null, decimal? estoque = null) =>
            {
                if (!Guid.TryParse(produtoId, out var productId) || productId == Guid.Empty)
                    return "{\"erro\":\"produtoId inválido. Use buscar_produtos para obter o id.\"}";

                if (preco is null && estoque is null)
                    return "{\"erro\":\"informe preco ou estoque\"}";

                if (preco is < 0 || estoque is < 0)
                    return "{\"erro\":\"preço e estoque não podem ser negativos\"}";

                var product = await _repository.GetByIdAsync(productId);
                if (product is null || product.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"produto não encontrado nesta empresa\"}";

                if (preco is not null)
                    product.DefaultValue = preco.Value;

                if (estoque is not null)
                    product.StorageQuantity = estoque.Value;

                var updated = await _service.UpdateAsync(product);

                return JsonSerializer.Serialize(
                    new { id = updated.Id, nome = updated.Name, preco = updated.DefaultValue, estoque = updated.StorageQuantity },
                    AiJson.Options);
            }, Name, Description);
    }
}
