using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface ICustomerSummaryService
    {
        /// <param name="includeFinancial">
        /// False leaves <see cref="CustomerSummaryDTO.Financial"/> null. The controller passes the
        /// caller's bills.read here: reading a customer is not the same permission as reading what
        /// they owe.
        /// </param>
        Task<CustomerSummaryDTO?> GetSummaryAsync(
            Guid customerId,
            Guid enterpriseId,
            bool includeFinancial,
            CancellationToken cancellation = default);
    }
}
