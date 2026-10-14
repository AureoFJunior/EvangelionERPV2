using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IPayableBillService
    {
        /// <param name="keepProvidedAmount">
        /// Keeps <see cref="PayableBill.Amount"/> as given even when item lines are attached. The
        /// supplier-NF-e import needs it: a nota's vNF carries freight, insurance and ST that no
        /// line value contains, and the payable is what gets paid.
        /// </param>
        Task<PayableBill> CreateAsync(PayableBill payableBill, bool useAmbientTransaction = false, bool keepProvidedAmount = false);
        Task<IEnumerable<PayableBill>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null, bool? isActive = true, int? billType = null, int? year = null, int? month = null, bool includePayroll = true);
        Task<PayableBill?> GetByIdAsync(Guid id, Guid enterpriseId);
        Task<PayableBill> UpdateAsync(PayableBill payableBill, Guid enterpriseId);
        Task<PayableBill> MarkProductsReceivedAsync(Guid id, Guid enterpriseId);
        Task<PayableBill> RefundAsync(Guid id, Guid enterpriseId, string reason);
        Task<IEnumerable<ReplenishmentSuggestionDTO>> GetReplenishmentSuggestionsAsync(Guid enterpriseId, ReplenishmentSuggestionRequestDTO request);
        Task<PayableBill> DeleteAsync(Guid id, Guid enterpriseId);
    }
}
