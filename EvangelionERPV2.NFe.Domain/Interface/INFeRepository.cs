using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Domain.Interface
{
    public interface INFeRepository<TEntity> where TEntity : class
    {
        Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null);

        Task<NFeDocument?> GetByAccessKeyAsync(string accessKey);

        /// <summary>
        /// Off-line contingency notes that still owe SEFAZ a transmission, oldest first.
        /// </summary>
        Task<IReadOnlyList<NFeDocument>> GetUntransmittedContingencyAsync(int take = 50);

        /// <summary>Tenant-scoped lookup, so the caller cannot forget the enterprise predicate.</summary>
        Task<NFeDocument?> GetByAccessKeyAsync(string accessKey, Guid enterpriseId);

        /// <summary>
        /// Pending documents the reconciler must resolve, in either of the two recoverable states:
        /// transmitted with an unknown outcome (consult SEFAZ, never retransmit) and never
        /// transmitted at all (safe to retransmit the same number).
        /// </summary>
        Task<IReadOnlyList<NFeDocument>> GetStalePendingAsync(TimeSpan olderThan, int maxAttempts, int take = 50);

        /// <summary>
        /// Atomically consumes and returns the next nNF for the tuple.
        /// </summary>
        /// <returns>The allocated number, or null when the counter row does not exist.</returns>
        Task<int?> AllocateNextNumberAsync(
            Guid enterpriseId,
            short model,
            string series,
            byte environment,
            CancellationToken cancellationToken = default);
    }
}
