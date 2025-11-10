
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.ProductModule.Application.Interface
{
    public interface IProductReportGeneratorService
    {
        Task<string> GenerateStockReportAsync(Enterprise enterprise);
    }
}