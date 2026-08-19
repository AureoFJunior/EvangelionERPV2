using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.BillsModule.Domain.Repositories
{
    public class BillsRepository : Repository<Bill>, IBillsRepository<Bill>
    {
        /// <summary>
        /// The NossoNumero allocation statement. One statement, one round trip:
        /// <c>UPDATE ... OUTPUT deleted</c> is atomic at any isolation level, so two API instances
        /// generating a boleto at the same moment cannot receive the same number. <c>ROWLOCK</c>
        /// keeps the engine from escalating to a page lock and serialising unrelated wallets.
        /// </summary>
        /// <remarks>
        /// Lifted wholesale from <c>NFeRepository.AllocateNextNumberSql</c> — same problem, so the
        /// same proven statement rather than a second mechanism to get wrong.
        ///
        /// The one addition is <c>NextNumber &lt;= Ceiling</c>: an exhausted convênio range must
        /// hand out nothing rather than a number the bank will not honour, and putting the check in
        /// the WHERE keeps it inside the same atomic statement instead of a read that could race.
        ///
        /// Public only so <c>BillNumberAllocationSqlTests</c> can pin its shape. Every clause is
        /// load-bearing for collision safety and this is the one piece with no integration test
        /// behind it, so the text is asserted instead. The caller must execute it with
        /// <c>ToListAsync</c> and compose nothing onto the <c>IQueryable</c> — a non-composable raw
        /// SQL query throws at runtime the moment anything is appended.
        /// </remarks>
        public const string AllocateNextNumberSql = """
            UPDATE dbo.BillNumberSequence WITH (ROWLOCK)
               SET NextNumber = NextNumber + 1
            OUTPUT deleted.NextNumber AS Value
             WHERE EnterpriseId = @enterpriseId
               AND BankCode = @bankCode
               AND Wallet = @wallet
               AND NextNumber <= Ceiling;
            """;

        public BillsRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<Bill?> GetByOrderIdAsync(Guid orderId)
        {
            // Cancelled is excluded so a reissued order resolves to its current title, not to the
            // superseded one. Ordered because the filtered unique index only guarantees one
            // non-cancelled row while the rows are live, not while a reissue is mid-flight.
            return await ActiveBills()
                .Where(x => x.OrderId == orderId && x.Status != BillStatus.Cancelled)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<IReadOnlyList<Bill>> GetAllByOrderIdAsync(Guid orderId)
        {
            if (orderId == Guid.Empty)
                return [];

            return await ActiveBills()
                .Where(x => x.OrderId == orderId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<long?> AllocateNextNumberAsync(
            Guid enterpriseId,
            int bankCode,
            string wallet,
            CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty || bankCode <= 0)
                return null;

            var allocated = await _context.Database
                .SqlQueryRaw<long>(
                    AllocateNextNumberSql,
                    new SqlParameter("@enterpriseId", enterpriseId),
                    new SqlParameter("@bankCode", bankCode),
                    new SqlParameter("@wallet", wallet ?? string.Empty))
                .ToListAsync(cancellationToken);

            return allocated.Count == 0 ? null : allocated[0];
        }

        /// <summary>Soft-delete filter plus AsNoTracking, applied by every read here.</summary>
        private IQueryable<Bill> ActiveBills() =>
            _context.Set<Bill>()
                .AsNoTracking()
                .Where(x => x.IsActive == null || x.IsActive == true);
    }
}
