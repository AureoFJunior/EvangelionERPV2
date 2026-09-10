using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class ResumoDeVendasTool : IAiTool
    {
        private readonly ISalesPerformanceService _service;

        public ResumoDeVendasTool(ISalesPerformanceService service) => _service = service;

        public string Name => "resumo_de_vendas";

        public string Description =>
            "Resumo de vendas de uma competência (mês), por vendedor, com meta e comissão. " +
            "Parâmetros: ano e mes. Se não informados, usa a competência atual.";

        public string RequiredPermission => "sales.performance.read";

        public bool RequiresConfirmation => false;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (int? ano = null, int? mes = null) =>
            {
                var now = DateTime.UtcNow;
                var year = ano is >= 2000 and <= 2100 ? ano.Value : now.Year;
                var month = mes is >= 1 and <= 12 ? mes.Value : now.Month;

                try
                {
                    var performance = await _service.GetPerformanceAsync(context.EnterpriseId, year, month);
                    return JsonSerializer.Serialize(performance, AiJson.Options);
                }
                catch (NotFoundDatabaseException)
                {
                    // SalesPerformanceService reads through a repository that throws on zero rows
                    // instead of returning an empty result. No sales this month is a normal answer.
                    return "{\"erro\":\"sem dados para o período\"}";
                }
            }, Name, Description);
    }
}
