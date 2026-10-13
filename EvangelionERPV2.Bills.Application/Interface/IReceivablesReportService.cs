using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IReceivablesReportService
    {
        Task<ReceivablesAgingDTO> GetAgingAsync(Guid enterpriseId, CancellationToken cancellation = default);

        /// <summary>
        /// One customer's aging buckets, counted in SQL.
        /// </summary>
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
