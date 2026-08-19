using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Domain.Interface
{
    public interface INFeRepository<TEntity> where TEntity : class
    {
        Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null);

        Task<NFeDocument?> GetByAccessKeyAsync(string accessKey);

        /// <summary>Tenant-scoped lookup, so the caller cannot forget the enterprise predicate.</summary>
        Task<NFeDocument?> GetByAccessKeyAsync(string accessKey, Guid enterpriseId);

        /// <summary>
        /// Pending documents the reconciler must resolve, in either of the two recoverable states:
        /// transmitted with an unknown outcome (consult SEFAZ, never retransmit) and never
        /// transmitted at all (safe to retransmit the same number).
        /// </summary>
        /// <remarks>
        /// Both halves are required. Filtering on <c>TransmittedAtUtc != null</c> left the
        /// "provably never sent" case with no owner: a crash between persisting the access key and
        /// setting the write-ahead marker stranded the row forever, because the sweeper skipped it
        /// and <c>IssueAsync</c>'s existing-Pending branch only polls briefly and then throws.
        /// Check <see cref="NFeDocument.IsOutcomeUnknown"/> per row to tell the two apart.
        /// Staleness is measured from the transmission when there was one, and from creation
        /// otherwise — <c>UpdatedAt</c> is unusable here, it defaults to <c>DateTime.MinValue</c>.
        /// </remarks>
        Task<IReadOnlyList<NFeDocument>> GetStalePendingAsync(TimeSpan olderThan, int maxAttempts, int take = 50);

        /// <summary>
        /// Atomically consumes and returns the next nNF for the tuple.
        /// </summary>
        /// <remarks>
        /// A single <c>UPDATE ... OUTPUT deleted.NextNumber</c> is atomic at any isolation level
        /// and correct across API instances. The process-static semaphores in NFeService are not
        /// a substitute: they are in-process and keyed by order, while numbering has to serialise
        /// per (enterprise, model, series).
        /// </remarks>
        /// <returns>The allocated number, or null when the counter row does not exist.</returns>
        Task<int?> AllocateNextNumberAsync(
            Guid enterpriseId,
            short model,
            string series,
            byte environment,
            CancellationToken cancellationToken = default);
    }
}
