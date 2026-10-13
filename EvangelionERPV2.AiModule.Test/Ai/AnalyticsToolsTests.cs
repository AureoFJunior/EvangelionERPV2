using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.Extensions.AI;
using Moq;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class AnalyticsToolsTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();
        private static readonly AiToolContext Context = new(Tenant, Guid.NewGuid());

        [Fact]
        public void EveryAnalyticsTool_DeclaresTheRightPermission()
        {
            Assert.Equal("sales.performance.read",
                new ResumoDeVendasTool(new Mock<ISalesPerformanceService>().Object).RequiredPermission);
            Assert.Equal("dashboard.read",
                new PainelDoDiaTool(new Mock<IDailyPanelService>().Object).RequiredPermission);
            Assert.Equal("bills.read",
                new ContasAReceberTool(new Mock<ICustomerSummaryService>().Object).RequiredPermission);
            Assert.Equal("cashflow.forecast.read",
                new PrevisaoDeCaixaTool(new Mock<ICashFlowForecastService>().Object).RequiredPermission);
            Assert.Equal("products.read",
                new EstoqueBaixoTool(new Mock<IPayableBillService>().Object).RequiredPermission);
        }

        [Fact]
        public async Task PrevisaoDeCaixa_PassesTheCallersTenantNotAnArgument()
        {
            var service = new Mock<ICashFlowForecastService>();
            service
                .Setup(x => x.GetForecastAsync(Tenant, It.IsAny<int>(), null))
                .ReturnsAsync(new CashFlowForecastDTO());

            var function = new PrevisaoDeCaixaTool(service.Object).Create(Context);
            await function.InvokeAsync(new AIFunctionArguments { ["horizonteEmDias"] = 30 });

            service.Verify(x => x.GetForecastAsync(Tenant, 30, null), Times.Once);
        }

        [Fact]
        public async Task PrevisaoDeCaixa_WithNoArguments_UsesTheDocumentedDefaultHorizon()
        {
            var service = new Mock<ICashFlowForecastService>();
            service
                .Setup(x => x.GetForecastAsync(Tenant, It.IsAny<int>(), null))
                .ReturnsAsync(new CashFlowForecastDTO());

            var function = new PrevisaoDeCaixaTool(service.Object).Create(Context);
            await function.InvokeAsync(new AIFunctionArguments());

            service.Verify(x => x.GetForecastAsync(Tenant, 30, null), Times.Once);
        }

        [Fact]
        public async Task PrevisaoDeCaixa_ClampsAnAbsurdHorizon()
        {
            var service = new Mock<ICashFlowForecastService>();
            service
                .Setup(x => x.GetForecastAsync(Tenant, It.IsAny<int>(), null))
                .ReturnsAsync(new CashFlowForecastDTO());

            var function = new PrevisaoDeCaixaTool(service.Object).Create(Context);
            await function.InvokeAsync(new AIFunctionArguments { ["horizonteEmDias"] = 99999 });

            service.Verify(x => x.GetForecastAsync(Tenant, 90, null), Times.Once);
        }

        [Fact]
        public async Task PrevisaoDeCaixa_WhenServiceThrowsNotFound_ReportsEmptyRatherThanFailing()
        {
            var service = new Mock<ICashFlowForecastService>();
            service
                .Setup(x => x.GetForecastAsync(Tenant, It.IsAny<int>(), null))
                .ThrowsAsync(new NotFoundDatabaseException());

            var function = new PrevisaoDeCaixaTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments { ["horizonteEmDias"] = 30 });

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task ResumoDeVendas_DefaultsToTheCurrentCompetencia()
        {
            var service = new Mock<ISalesPerformanceService>();
            service
                .Setup(x => x.GetPerformanceAsync(Tenant, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SalesPerformanceDTO());

            var function = new ResumoDeVendasTool(service.Object).Create(Context);
            await function.InvokeAsync(new AIFunctionArguments());

            var now = DateTime.UtcNow;
            service.Verify(x => x.GetPerformanceAsync(Tenant, now.Year, now.Month, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ResumoDeVendas_WhenServiceThrowsNotFound_ReportsEmptyRatherThanFailing()
        {
            var service = new Mock<ISalesPerformanceService>();
            service
                .Setup(x => x.GetPerformanceAsync(Tenant, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var function = new ResumoDeVendasTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments());

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task PainelDoDia_PassesTheCallersTenant()
        {
            var service = new Mock<IDailyPanelService>();
            service
                .Setup(x => x.GetTodayAsync(Tenant, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DailyPanelDTO());

            var function = new PainelDoDiaTool(service.Object).Create(Context);
            await function.InvokeAsync(new AIFunctionArguments());

            service.Verify(x => x.GetTodayAsync(Tenant, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PainelDoDia_WhenServiceThrowsNotFound_ReportsEmptyRatherThanFailing()
        {
            var service = new Mock<IDailyPanelService>();
            service
                .Setup(x => x.GetTodayAsync(Tenant, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var function = new PainelDoDiaTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments());

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task ContasAReceber_PassesTheCallersTenantAndIncludesFinancial()
        {
            var customerId = Guid.NewGuid();
            var service = new Mock<ICustomerSummaryService>();
            service
                .Setup(x => x.GetSummaryAsync(customerId, Tenant, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CustomerSummaryDTO());

            var function = new ContasAReceberTool(service.Object).Create(Context);
            await function.InvokeAsync(new AIFunctionArguments { ["clienteId"] = customerId.ToString() });

            service.Verify(x => x.GetSummaryAsync(customerId, Tenant, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ContasAReceber_WithInvalidId_ReturnsErrorRatherThanCallingTheService()
        {
            var service = new Mock<ICustomerSummaryService>();

            var function = new ContasAReceberTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments { ["clienteId"] = "not-a-guid" });

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
            service.Verify(
                x => x.GetSummaryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ContasAReceber_WhenServiceThrowsNotFound_ReportsErrorRatherThanFailing()
        {
            var customerId = Guid.NewGuid();
            var service = new Mock<ICustomerSummaryService>();
            service
                .Setup(x => x.GetSummaryAsync(customerId, Tenant, true, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var function = new ContasAReceberTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments { ["clienteId"] = customerId.ToString() });

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task EstoqueBaixo_WhenServiceThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var service = new Mock<IPayableBillService>();
            service
                .Setup(x => x.GetReplenishmentSuggestionsAsync(Tenant, It.IsAny<ReplenishmentSuggestionRequestDTO>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var function = new EstoqueBaixoTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments());

            Assert.Contains("\"total\":0", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task EstoqueBaixo_WithMoreThanTheDisplayCap_ReportsTruncated()
        {
            var service = new Mock<IPayableBillService>();
            service
                .Setup(x => x.GetReplenishmentSuggestionsAsync(Tenant, It.IsAny<ReplenishmentSuggestionRequestDTO>()))
                .ReturnsAsync(Enumerable.Range(1, 21).Select(i => new ReplenishmentSuggestionDTO { ProductId = Guid.NewGuid() }));

            var function = new EstoqueBaixoTool(service.Object).Create(Context);
            var result = await function.InvokeAsync(new AIFunctionArguments());

            var json = result?.ToString() ?? string.Empty;
            Assert.Contains("\"mostrando\":20", json);
            Assert.Contains("\"truncado\":true", json);
        }
    }
}
