using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IPayableBillService
    {
        Task<PayableBill> CreateAsync(PayableBill payableBill);
        Task<IEnumerable<PayableBill>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null, bool? isActive = true, int? billType = null);
        Task<PayableBill?> GetByIdAsync(Guid id, Guid enterpriseId);
        Task<PayableBill> UpdateAsync(PayableBill payableBill, Guid enterpriseId);
        Task<PayableBill> MarkProductsReceivedAsync(Guid id, Guid enterpriseId);
        Task<PayableBill> RefundAsync(Guid id, Guid enterpriseId, string reason);
        Task<IEnumerable<ReplenishmentSuggestionDTO>> GetReplenishmentSuggestionsAsync(Guid enterpriseId, ReplenishmentSuggestionRequestDTO request);
        Task<PayableBill> DeleteAsync(Guid id, Guid enterpriseId);
    }
}
