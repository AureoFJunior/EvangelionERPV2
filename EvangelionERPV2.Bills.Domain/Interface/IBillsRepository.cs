using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Domain.Interface
{
    public interface IBillsRepository<TEntity> where TEntity : class
    {
        #region Async
        /// <summary>The order's live boleto — the one that is not cancelled. Null once every one was superseded.</summary>
        Task<Bill?> GetByOrderIdAsync(Guid orderId);

        /// <summary>
        /// Every boleto ever issued for the order, newest first, cancelled ones included. A payer can
        /// still be holding a superseded title and settle it, so the history has to stay reachable.
        /// </summary>
        Task<IReadOnlyList<Bill>> GetAllByOrderIdAsync(Guid orderId);

        /// <summary>
        /// Hands out the next NossoNumero for the (enterprise, bank, wallet) range.
        /// Null when there is no counter row yet, or when the contracted range is exhausted — the
        /// caller separates the two, because one is fixable by creating the row and the other is not.
        /// </summary>
        Task<long?> AllocateNextNumberAsync(
            Guid enterpriseId,
            int bankCode,
            string wallet,
            CancellationToken cancellationToken = default);
        #endregion
    }
}
