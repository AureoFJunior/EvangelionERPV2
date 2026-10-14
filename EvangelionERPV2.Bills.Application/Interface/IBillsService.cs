using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IBillsService<TEntity> where TEntity : class
    {
        Task<Bill?> GetByOrderIdAsync(Guid orderId, Guid enterpriseId);
        Task<Bill?> GenerateAsync(Guid orderId, Guid enterpriseId);
        Task<byte[]?> GetPdfAsync(Guid orderId, Guid enterpriseId);

        /// <summary>Every boleto issued for the order, newest first, superseded ones included.</summary>
        Task<IReadOnlyList<Bill>> GetHistoryAsync(Guid orderId, Guid enterpriseId);

        /// <summary>
        /// Records a settlement against one boleto, by bill id rather than order id: a payer can
        /// settle a title that has since been superseded, and that payment still has to land
        /// somewhere.
        /// </summary>
        Task<Bill?> SettleAsync(Guid billId, Guid enterpriseId, decimal paidAmount, DateTime? paidAt, string? settlementProtocol);

        /// <summary>
        /// Cancels the order's live boleto and issues a fresh one from the order's current dates.
        /// Supersedes rather than mutates, so a payer holding the old document can still be
        /// reconciled against it.
        /// </summary>
        Task<Bill?> ReissueAsync(Guid orderId, Guid enterpriseId);
    }
}
