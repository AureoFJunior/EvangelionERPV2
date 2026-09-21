using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.OrderModule.Domain.Repositories
{
    public class QuoteRepository : Repository<Quote>, IQuoteRepository
    {
        /// <summary>
        /// The quote-number allocation statement. One statement, one round trip:
        /// <c>UPDATE ... OUTPUT deleted</c> is atomic at any isolation level, so two salespeople
        /// saving at the same moment cannot both be handed quote 41. <c>ROWLOCK</c> keeps the
        /// engine from escalating to a page lock and serialising unrelated tenants.
        /// </summary>
        /// <remarks>
        /// Lifted from <c>BillsRepository.AllocateNextNumberSql</c>, which lifted it from
        /// <c>NFeRepository</c> — same problem, so the same proven statement rather than a third
        /// mechanism to get wrong. There is no ceiling here: a quote number is not fiscal and
        /// nothing rejects a gap or an overflow the way a convênio range does.
        ///
        /// Public only so <c>QuoteNumberAllocationSqlTests</c> can pin its shape, exactly as the
        /// boleto one is. Execute with <c>ToListAsync</c> and compose nothing onto the
        /// <c>IQueryable</c> — a non-composable raw SQL query throws the moment anything is added.
        /// </remarks>
        public const string AllocateNextNumberSql = """
            UPDATE dbo.QuoteNumberSequence WITH (ROWLOCK)
               SET NextNumber = NextNumber + 1
            OUTPUT deleted.NextNumber AS Value
             WHERE EnterpriseId = @enterpriseId;
            """;

        public QuoteRepository(AppDbContext context) : base(context)
        {
        }

        /// <summary>
        /// The next number for this tenant, creating the counter the first time it is asked for.
        /// </summary>
        public async Task<long> AllocateNextNumberAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new ArgumentException("Enterprise is required.", nameof(enterpriseId));

            var allocated = await AllocateAsync(enterpriseId, cancellationToken);
            if (allocated != null)
                return allocated.Value;

            try
            {
                await _context.Set<QuoteNumberSequence>().AddAsync(
                    new QuoteNumberSequence { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, NextNumber = 1, IsActive = true },
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
            }

            return await AllocateAsync(enterpriseId, cancellationToken)
                ?? throw new InvalidOperationException("The quote counter could not be allocated.");
        }

        public async Task<Quote?> GetWithItemsAsync(Guid id, Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            return await _context.Set<Quote>()
                .AsNoTracking()
                .Include(quote => quote.Items!)
                    .ThenInclude(item => item.Product)
                .Include(quote => quote.Customer)
                .Include(quote => quote.Enterprise)
                .AsSplitQuery()
                .FirstOrDefaultAsync(
                    quote => quote.Id == id
                             && quote.EnterpriseId == enterpriseId
                             && (quote.IsActive == null || quote.IsActive == true),
                    cancellationToken);
        }

        public async Task<IReadOnlyList<Quote>> GetByEnterpriseAsync(
            Guid enterpriseId,
            int? status,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Set<Quote>()
                .AsNoTracking()
                .Include(quote => quote.Customer)
                .Where(quote => quote.EnterpriseId == enterpriseId && (quote.IsActive == null || quote.IsActive == true));

            if (status.HasValue)
                query = query.Where(quote => quote.Status == status.Value);

            return await query
                .OrderByDescending(quote => quote.Number)
                .ToListAsync(cancellationToken);
        }

        private async Task<long?> AllocateAsync(Guid enterpriseId, CancellationToken cancellationToken)
        {
            var allocated = await _context.Database
                .SqlQueryRaw<long>(AllocateNextNumberSql, new SqlParameter("@enterpriseId", enterpriseId))
                .ToListAsync(cancellationToken);

            return allocated.Count == 0 ? null : allocated[0];
        }
    }
}
