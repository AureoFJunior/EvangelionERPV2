using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IReceivablesReportService
    {
        Task<ReceivablesAgingDTO> GetAgingAsync(Guid enterpriseId, CancellationToken cancellation = default);

        /// <summary>
        /// One customer's aging buckets, counted in SQL.
        /// </summary>
        /// <remarks>
        /// <see cref="GetAgingAsync"/> builds a position object per open order to read one row out
        /// of the result, which costs a gigabyte on a customer with 1.8M of them. Same bucket
        /// boundaries and the same receivable rule, aggregated instead of materialised.
        /// </remarks>
        Task<ReceivablesAgingCustomerDTO> GetCustomerAgingAsync(
            Guid enterpriseId,
            Guid customerId,
            CancellationToken cancellation = default);

        Task<IReadOnlyList<DelinquentCustomerDTO>> GetDelinquentAsync(
            Guid enterpriseId,
            int minimumDaysOverdue,
            CancellationToken cancellation = default);

        Task<CustomerStatementDTO?> GetStatementAsync(
            Guid enterpriseId,
            Guid customerId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellation = default);
    }
}
