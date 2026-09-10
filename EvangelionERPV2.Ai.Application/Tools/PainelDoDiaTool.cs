using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    /// <summary>
    /// The one tool worth calling when the question is vague: "como estamos hoje" has an answer
    /// already assembled by the dashboard, and it is a better one than five separate lookups.
    /// </summary>
    public sealed class PainelDoDiaTool : IAiTool
    {
        private readonly IDailyPanelService _service;

        public PainelDoDiaTool(IDailyPanelService service) => _service = service;

        public string Name => "painel_do_dia";

        public string Description =>
            "Panorama do dia: recebimentos, pagamentos dos próximos 7 dias, estoque crítico, " +
            "pendências fiscais e lançamentos bancários não conciliados. Sem parâmetros. " +
            "Use quando a pergunta for geral sobre a situação da empresa.";

        public string RequiredPermission => "dashboard.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async () =>
            {
                try
                {
                    var panel = await _service.GetTodayAsync(context.EnterpriseId);
                    return JsonSerializer.Serialize(panel, AiJson.Options);
                }
                catch (NotFoundDatabaseException)
                {
                    // The panel aggregates several repository reads, any of which can throw on
                    // zero rows. Nothing to report is itself the honest answer.
                    return "{\"erro\":\"sem dados para hoje\"}";
                }
            }, Name, Description);
    }
}
