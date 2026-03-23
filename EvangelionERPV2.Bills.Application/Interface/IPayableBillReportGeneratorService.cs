using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IPayableBillReportGeneratorService
    {
        Task<string> GeneratePayablesOverviewReportAsync(Enterprise enterprise);
    }
}
