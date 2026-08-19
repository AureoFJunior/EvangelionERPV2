using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.NFeModule.Domain.Repositories
{
    public class NFeRepository : Repository<NFeDocument>, INFeRepository<NFeDocument>
    {

        /// <summary>
        /// The number allocation statement. One statement, one round trip:
        /// <c>UPDATE ... OUTPUT deleted</c> is atomic at any isolation level, so two API instances
        /// issuing at the same moment cannot receive the same nNF. <c>ROWLOCK</c> keeps the engine
        /// from escalating to a page lock and serialising unrelated series.
        /// </summary>
        /// <remarks>
        /// Public only so <c>NFeNumberAllocationSqlTests</c> can pin its shape. Every clause here is
        /// load-bearing for duplicate-emission safety and this is the one piece with no integration
        /// test behind it, so the text is asserted instead. Note that the caller must execute it with
        /// <c>ToListAsync</c> and compose nothing onto the <c>IQueryable</c> — a non-composable raw
        /// SQL query throws at runtime the moment anything is appended.
        /// </remarks>
        public const string AllocateNextNumberSql = """
            UPDATE dbo.NFeNumberSequence WITH (ROWLOCK)
               SET NextNumber = NextNumber + 1
            OUTPUT deleted.NextNumber AS Value
             WHERE EnterpriseId = @enterpriseId
               AND Model = @model
               AND Series = @series
               AND Environment = @environment;
            """;

        public NFeRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null)
        {
            var query = ActiveDocuments().Where(x => x.OrderId == orderId);

            if (type.HasValue)
                query = query.Where(x => x.Type == type.Value);

            return await query.FirstOrDefaultAsync();
        }

        public async Task<NFeDocument?> GetByAccessKeyAsync(string accessKey)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            return await ActiveDocuments().FirstOrDefaultAsync(x => x.AccessKey == accessKey);
        }

        public async Task<NFeDocument?> GetByAccessKeyAsync(string accessKey, Guid enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(accessKey) || enterpriseId == Guid.Empty)
                return null;

            return await ActiveDocuments()
                .FirstOrDefaultAsync(x => x.AccessKey == accessKey && x.EnterpriseId == enterpriseId);
        }

        public async Task<IReadOnlyList<NFeDocument>> GetStalePendingAsync(
            TimeSpan olderThan, int maxAttempts, int take = 50)
        {
            var cutoff = DateTime.UtcNow - olderThan;

            // Both recoverable states, not just the transmitted one — see INFeRepository. The
            // staleness clock is the transmission when there was one and creation otherwise, which
            // COALESCEs in SQL; UpdatedAt cannot be used, it defaults to DateTime.MinValue and would
            // make every freshly claimed row look stale immediately.
            return await ActiveDocuments()
                .Where(x => x.Status == NFeStatus.Pending
                            && (x.TransmittedAtUtc ?? x.CreatedAt) <= cutoff
                            && x.AttemptCount < maxAttempts)
                .OrderBy(x => x.TransmittedAtUtc ?? x.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int?> AllocateNextNumberAsync(
            Guid enterpriseId,
            short model,
            string series,
            byte environment,
            CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty || string.IsNullOrWhiteSpace(series))
                return null;

            var allocated = await _context.Database
                .SqlQueryRaw<int>(
                    AllocateNextNumberSql,
                    new SqlParameter("@enterpriseId", enterpriseId),
                    new SqlParameter("@model", model),
                    new SqlParameter("@series", series),
                    new SqlParameter("@environment", environment))
                .ToListAsync(cancellationToken);

            // Null rather than an exception: a missing counter means the tenant never saved a
            // fiscal profile, which the caller reports as a configuration problem, not a fault.
            return allocated.Count == 0 ? null : allocated[0];
        }

        /// <summary>Soft-delete filter plus AsNoTracking, applied by every read here.</summary>
        private IQueryable<NFeDocument> ActiveDocuments() =>
            _context.Set<NFeDocument>()
                .AsNoTracking()
                .Where(x => x.IsActive == null || x.IsActive == true);
    }
}
