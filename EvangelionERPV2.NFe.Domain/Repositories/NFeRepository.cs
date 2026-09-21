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

            return await ActiveDocuments()
                .Where(x => x.Status == NFeStatus.Pending
                            && (x.TransmittedAtUtc ?? x.CreatedAt) <= cutoff
                            && x.AttemptCount < maxAttempts)
                .OrderBy(x => x.TransmittedAtUtc ?? x.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<NFeDocument>> GetUntransmittedContingencyAsync(int take = 50)
        {
            return await ActiveDocuments()
                .Where(x => x.TipoEmissao == 9
                            && x.ContingencyEnteredAt != null
                            && x.Status != NFeStatus.Authorized
                            && x.Status != NFeStatus.Cancelled

                            && x.TransmittedAtUtc == null)
                .OrderBy(x => x.ContingencyEnteredAt)
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

            return allocated.Count == 0 ? null : allocated[0];
        }

        /// <summary>Soft-delete filter plus AsNoTracking, applied by every read here.</summary>
        private IQueryable<NFeDocument> ActiveDocuments() =>
            _context.Set<NFeDocument>()
                .AsNoTracking()
                .Where(x => x.IsActive == null || x.IsActive == true);
    }
}
