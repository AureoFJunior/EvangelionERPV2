using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class EstoqueBaixoTool : IAiTool
    {
        private const int PageSize = AiJson.DefaultCap;

        private const int FetchSize = PageSize + 1;

        private readonly IPayableBillService _service;

        public EstoqueBaixoTool(IPayableBillService service) => _service = service;

        public string Name => "estoque_baixo";

        public string Description =>
            "Produtos que estão perto de acabar, com a quantidade sugerida de compra e quantos " +
            "dias de cobertura restam. Sem parâmetros. Use quando perguntarem o que precisa comprar.";

        public string RequiredPermission => "products.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async () =>
            {
                var suggestions = await AiQuery.OrEmptyAsync(() => _service.GetReplenishmentSuggestionsAsync(
                    context.EnterpriseId,
                    new ReplenishmentSuggestionRequestDTO { PageNumber = 1, PageSize = FetchSize, SortByCriticality = true }));

                return AiJson.ForModel(suggestions, suggestions.Count);
            }, Name, Description);
    }
}
