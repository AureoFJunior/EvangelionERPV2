
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.OrderModule.Application.Interface
{
    public interface IOrderReportGeneratorService
    {
        Task<string> GenerateMonthlyBillingReportAsync(Enterprise enterprise, IEnumerable<Order> orders);
    }
}