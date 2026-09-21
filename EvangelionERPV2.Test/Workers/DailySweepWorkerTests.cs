using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.Test.Workers
{
    /// <summary>
    /// Covers the schedule and the lease, never SMTP: the pass itself is <c>DunningService</c>'s
    /// and is tested there.
    /// </summary>
    public class DailySweepWorkerTests
    {
        [Theory]
        [InlineData("2026-08-21T09:30:00Z", "2026-08-21T11:00:00Z")]
        [InlineData("2026-08-21T10:59:59Z", "2026-08-21T11:00:00Z")]
        [InlineData("2026-08-21T11:00:00Z", "2026-08-22T11:00:00Z")]
        [InlineData("2026-08-21T23:59:00Z", "2026-08-22T11:00:00Z")]
        public void TimeUntilNextRun_TargetsTheConfiguredDailySlot(string utcNow, string expectedSlot)
        {
            var now = DateTime.Parse(utcNow).ToUniversalTime();
            var worker = BuildWorker(new DunningSettings { Enabled = true, SweepHourUtc = 11 });

            Assert.Equal(DateTime.Parse(expectedSlot).ToUniversalTime(), now + worker.TimeUntilNextRun(now));
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(24)]
        [InlineData(99)]
        public void TimeUntilNextRun_ClampsANonsenseHourInsteadOfThrowing(int configuredHour)
        {
            var worker = BuildWorker(new DunningSettings { Enabled = true, SweepHourUtc = configuredHour });
            var delay = worker.TimeUntilNextRun(DateTime.Parse("2026-08-21T09:30:00Z").ToUniversalTime());

            Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromHours(24));
        }

        [Fact]
        public void LeaseKey_IsItsOwn()
        {
            Assert.Equal("eva:lease:daily-sweep", BuildWorker(new DunningSettings()).LeaseKey);
        }

        [Fact]
        public async Task RunOnceUnderLease_ShouldNotRun_WhenTheLeaseIsHeldElsewhere()
        {
            var lease = new Mock<IDistributedLease>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync((string?)null);

            var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
            var worker = new DailySweepWorker(
                scopeFactory.Object,
                Options.Create(new DunningSettings { Enabled = true }),
                lease.Object);

            Assert.False(await worker.RunOnceUnderLeaseAsync(TimeSpan.FromHours(24), CancellationToken.None));

            scopeFactory.VerifyNoOtherCalls();
        }

        private static DailySweepWorker BuildWorker(DunningSettings settings) => new(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(settings),
            Mock.Of<IDistributedLease>());
    }
}
