using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>
    /// A counted correction, the same thing the product screen does when someone edits the quantity:
    /// a delta through <see cref="IProductService{T}.ApplyStockMovementAsync"/>, which writes the
    /// ledger row that says why. It is NOT how bought stock arrives — that is receber_compra.
    /// </summary>
    public sealed class AjustarEstoqueTool : IAiTool
    {
        private readonly IProductService<Product> _service;
        private readonly IRepository<Product> _repository;

        public AjustarEstoqueTool(IProductService<Product> service, IRepository<Product> repository)
        {
            _service = service;
            _repository = repository;
        }

        public string Name => "ajustar_estoque";

        public string Description =>
            "Corrige a contagem de estoque de um produto, como um inventário. Parâmetros: produtoId " +
            "(id ou nome — o sistema resolve) e novoEstoque (a quantidade que existe de verdade na " +
            "prateleira). Registra um movimento de Ajuste no histórico. Para mercadoria comprada que " +
            "chegou, use receber_compra em vez desta. Só acontece depois que a pessoa confirmar.";

        public string RequiredPermission => "products.update";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            $"Corrigir o estoque do produto {AiArguments.Text(arguments, "produtoId")} para "
            + $"{AiArguments.Text(arguments, "novoEstoque")} (movimento de ajuste no histórico)";

        public async Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default)
        {
            var target = AiArguments.Number(arguments, "novoEstoque");

            if (target is null)
                return AiToolArguments.Refused("informe novoEstoque: a quantidade contada.");

            if (target < 0)
                return AiToolArguments.Refused("o estoque contado não pode ser negativo.");

            var found = await AiEntityLookup.ProductAsync(
                _repository, AiArguments.Text(arguments, "produtoId"), context.EnterpriseId);

            if (found.Error is not null)
                return AiToolArguments.Refused(found.Error);

            return AiToolArguments.Ok(AiArguments.With(arguments, "produtoId", found.Id!.Value.ToString()));
        }

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string produtoId, decimal novoEstoque) =>
            {
                if (!Guid.TryParse(produtoId, out var productId) || productId == Guid.Empty)
                    return "{\"erro\":\"produtoId inválido. Use buscar_produtos para obter o id.\"}";

                if (novoEstoque < 0)
                    return "{\"erro\":\"o estoque contado não pode ser negativo\"}";

                var product = await AiQuery.ByIdOrNullAsync(() => _repository.GetByIdAsync(productId));
                if (product is null || product.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"produto não encontrado nesta empresa\"}";

                if (product.IsActive != true)
                    return "{\"erro\":\"este produto está inativo e não pode ser alterado\"}";

                var delta = novoEstoque - product.StorageQuantity;
                if (delta == 0)
                    return "{\"erro\":\"nada para alterar: o estoque já está nessa quantidade\"}";

                var movement = await _service.ApplyStockMovementAsync(
                    productId,
                    context.EnterpriseId,
                    delta,
                    EnumStockMovementType.Adjustment,
                    referenceId: null);

                return JsonSerializer.Serialize(
                    new
                    {
                        id = productId,
                        nome = product.Name,
                        estoqueAnterior = product.StorageQuantity,
                        estoque = movement.BalanceAfter,
                        movimento = "Ajuste",
                    },
                    AiJson.Options);
            }, Name, Description);
    }
}
