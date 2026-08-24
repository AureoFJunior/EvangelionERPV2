using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.OrderModule.Application.Interface
{
    public interface IQuoteService
    {
        Task<IReadOnlyList<Quote>> GetByEnterpriseAsync(Guid enterpriseId, int? status, CancellationToken cancellation = default);
        Task<Quote?> GetByIdAsync(Guid id, Guid enterpriseId, CancellationToken cancellation = default);
        Task<Quote> CreateAsync(Guid enterpriseId, UpsertQuoteRequestDTO request, CancellationToken cancellation = default);
        Task<Quote> UpdateAsync(Guid id, Guid enterpriseId, UpsertQuoteRequestDTO request, CancellationToken cancellation = default);
        Task<Quote> SetStatusAsync(Guid id, Guid enterpriseId, EnumQuoteStatus status, CancellationToken cancellation = default);

        /// <param name="allowCreditOverride">
        /// Passed straight through to order creation: converting a quote is creating an order, so
        /// the customer's credit limit applies exactly as it would to a typed one.
        /// </param>
        Task<Quote> ConvertAsync(Guid id, Guid enterpriseId, ConvertQuoteRequestDTO request, bool allowCreditOverride = false, CancellationToken cancellation = default);
        Task<Quote> DeleteAsync(Guid id, Guid enterpriseId, CancellationToken cancellation = default);
    }
}
