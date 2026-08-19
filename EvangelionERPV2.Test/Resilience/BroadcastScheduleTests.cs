using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Resilience
{
    /// <summary>
    /// Broadcast workers used to sleep for a relative interval, so every container restart fired an
    /// extra email. The schedule must depend only on the wall clock.
    /// </summary>
    public class BroadcastScheduleTests
    {
        [Fact]
        public void TimeUntilNextWeekly_LandsOnTheConfiguredDayAndHour()
        {
            // Wednesday 2026-08-05 10:00 UTC.
            var now = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);

            var target = now.Add(BroadcastSchedule.TimeUntilNextWeekly(now, DayOfWeek.Monday, 8));

            Assert.Equal(DayOfWeek.Monday, target.DayOfWeek);
            Assert.Equal(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), target);
        }

        [Fact]
        public void TimeUntilNextWeekly_RightAfterTheSlot_WaitsAFullWeek()
        {
            // One second past Monday's slot: the run that just happened must not repeat today.
            var justSent = new DateTime(2026, 8, 10, 8, 0, 1, DateTimeKind.Utc);

            var target = justSent.Add(BroadcastSchedule.TimeUntilNextWeekly(justSent, DayOfWeek.Monday, 8));

            Assert.Equal(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc), target);
        }

        [Fact]
        public void TimeUntilNextWeekly_IsIndependentOfRestartCount()
        {
            var slot = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
            var firstStart = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);
            var restart = new DateTime(2026, 8, 7, 23, 30, 0, DateTimeKind.Utc);

            Assert.Equal(slot, firstStart.Add(BroadcastSchedule.TimeUntilNextWeekly(firstStart, DayOfWeek.Monday, 8)));
            Assert.Equal(slot, restart.Add(BroadcastSchedule.TimeUntilNextWeekly(restart, DayOfWeek.Monday, 8)));
        }

        [Fact]
        public void TimeUntilNextDaily_BeforeAndAfterTheSlot()
        {
            var beforeSlot = new DateTime(2026, 8, 5, 7, 30, 0, DateTimeKind.Utc);
            var afterSlot = new DateTime(2026, 8, 5, 8, 30, 0, DateTimeKind.Utc);

            Assert.Equal(TimeSpan.FromMinutes(30), BroadcastSchedule.TimeUntilNextDaily(beforeSlot, 8));
            Assert.Equal(TimeSpan.FromHours(23.5), BroadcastSchedule.TimeUntilNextDaily(afterSlot, 8));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(24)]
        public void TimeUntilNextDaily_RejectsAnInvalidHour(int hourUtc)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BroadcastSchedule.TimeUntilNextDaily(DateTime.UtcNow, hourUtc));
        }
    }
}
