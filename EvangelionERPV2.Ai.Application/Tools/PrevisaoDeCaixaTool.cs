using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class PrevisaoDeCaixaTool : IAiTool
    {
        private readonly ICashFlowForecastService _service;

        public PrevisaoDeCaixaTool(ICashFlowForecastService service) => _service = service;

        public string Name => "previsao_de_caixa";

        public string Description =>
            "Projeção do saldo em caixa dia a dia, somando recebíveis e subtraindo contas a pagar. " +
            "Parâmetro: horizonteEmDias (30, 60 ou 90; padrão 30). Marca os dias de risco.";

        public string RequiredPermission => "cashflow.forecast.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? horizonteEmDias) =>
            {
                // Clamped rather than validated: a model that asks for 99999 days meant "muito
                // tempo", and answering with 90 is more useful than refusing.
                var horizon = Math.Clamp(horizonteEmDias ?? 30, 1, 90);

                try
                {
                    var forecast = await _service.GetForecastAsync(context.EnterpriseId, horizon);
                    return JsonSerializer.Serialize(forecast);
                }
                catch (NotFoundDatabaseException)
                {
                    return "{\"erro\":\"sem dados para o período\"}";
                }
            }, Name, Description);
    }
}
