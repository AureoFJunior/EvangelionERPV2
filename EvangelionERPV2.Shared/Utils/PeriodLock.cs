using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The one rule behind fechamento de período: nothing may be written into a month the tenant
    /// has already handed the accountant.
    /// </summary>
    /// <remarks>
    /// A pure function rather than a SaveChanges interceptor. Enforcing it centrally would mean
    /// resolving the ambient tenant's lock date on every save, and special-casing Bill anyway —
    /// settling a boleto that fell due in a closed month is legitimate, the money arrived today.
    /// Three explicit call sites are smaller and say plainly which writes are governed.
    ///
    /// The comparison is by date: a lock through the 31st blocks the 31st itself.
    /// </remarks>
    public static class PeriodLock
    {
        public static void Assert(DateTime? closedThroughUtc, DateTime businessDate, string what)
        {
            if (closedThroughUtc == null)
                return;

            var closedThrough = closedThroughUtc.Value.Date;
            if (businessDate.Date > closedThrough)
                return;

            throw new PeriodClosedException(
                $"{what} is dated {businessDate:yyyy-MM-dd}, inside the period closed through {closedThrough:yyyy-MM-dd}.");
        }
    }
}
