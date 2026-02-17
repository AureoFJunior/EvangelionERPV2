using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IPayableBillService
    {
        Task<PayableBill> CreateAsync(PayableBill payableBill);
        Task<IEnumerable<PayableBill>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null);
        Task<PayableBill?> GetByIdAsync(Guid id, Guid enterpriseId);
        Task<PayableBill> UpdateAsync(PayableBill payableBill, Guid enterpriseId);
        Task<PayableBill> DeleteAsync(Guid id, Guid enterpriseId);
    }
}
