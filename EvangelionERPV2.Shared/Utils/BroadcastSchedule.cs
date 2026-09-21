namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// Delays anchored to wall-clock slots instead of "now + interval". A worker that sleeps for a
    /// relative interval restarts its countdown on every deploy, so each container restart fires an
    /// extra broadcast; anchoring to the calendar keeps the cadence at exactly one per slot no
    /// matter how often the process restarts.
    /// </summary>
    public static class BroadcastSchedule
    {
        public static TimeSpan TimeUntilNextWeekly(DateTime utcNow, DayOfWeek dayOfWeek, int hourUtc)
        {
            var next = NextDailySlot(utcNow, hourUtc);

            while (next.DayOfWeek != dayOfWeek)
                next = next.AddDays(1);

            return next - utcNow;
        }

        public static TimeSpan TimeUntilNextDaily(DateTime utcNow, int hourUtc)
        {
            return NextDailySlot(utcNow, hourUtc) - utcNow;
        }

        private static DateTime NextDailySlot(DateTime utcNow, int hourUtc)
        {
            if (hourUtc is < 0 or > 23)
                throw new ArgumentOutOfRangeException(nameof(hourUtc), "Hour must be between 0 and 23.");

            var slot = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, hourUtc, 0, 0, DateTimeKind.Utc);

            return slot <= utcNow ? slot.AddDays(1) : slot;
        }
    }
}
