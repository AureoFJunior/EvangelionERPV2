using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The one rule behind fechamento de período: nothing may be written into a month the tenant
    /// has already handed the accountant.
    /// </summary>
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
