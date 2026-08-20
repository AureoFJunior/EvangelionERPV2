using EvangelionERPV2.EmployeeModule.Application.Configs;
using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.EmployeeModule.Test
{
    /// <summary>
    /// The lapse check is the reason the accrual ledger exists: past the concessivo deadline, CLT
    /// art. 137 makes the outstanding days payable twice. Every test here is about which accruals
    /// the sweep touches, because a false positive invents a debt and a false negative hides one.
    /// </summary>
    public class EmployeeVacationLapseServiceTests
    {
        private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private static DateTime Today => DateTime.UtcNow.Date;

        #region Sweep

        [Fact]
        public async Task Sweep_WhenTheConcessivoClosedWithDaysOutstanding_MarksItLapsed()
        {
            var accrual = Accrual(deadline: Today.AddDays(-1), entitled: 30, taken: 10);
            var context = new LapseContext(accrual);

            var lapsed = await context.Service.SweepLapsedAsync();

            Assert.Equal(1, lapsed);
            Assert.Equal((int)EnumVacationAccrualStatus.Lapsed, accrual.Status);
            Assert.Equal(1, context.CommitCalls);
        }

        [Fact]
        public async Task Sweep_WhenNoDaysAreOutstanding_LeavesItAlone()
        {
            // Every entitled day accounted for: the deadline passed with nothing owed, which is the
            // ordinary happy ending, not a liability.
            var accrual = Accrual(deadline: Today.AddDays(-30), entitled: 30, taken: 20, sold: 10);
            var context = new LapseContext(accrual);

            var lapsed = await context.Service.SweepLapsedAsync();

            Assert.Equal(0, lapsed);
            Assert.NotEqual((int)EnumVacationAccrualStatus.Lapsed, accrual.Status);
            context.Repository.Verify(x => x.Update(It.IsAny<EmployeeVacationAccrual>()), Times.Never);
        }

        [Fact]
        public async Task Sweep_LeavesAClosedAccrualAlone()
        {
            var accrual = Accrual(deadline: Today.AddDays(-90), entitled: 30, taken: 5,
                status: EnumVacationAccrualStatus.Closed);
            var context = new LapseContext(accrual);

            var lapsed = await context.Service.SweepLapsedAsync();

            Assert.Equal(0, lapsed);
            Assert.Equal((int)EnumVacationAccrualStatus.Closed, accrual.Status);
        }

        [Fact]
        public async Task Sweep_DoesNotRewriteAnAlreadyLapsedAccrual()
        {
            var accrual = Accrual(deadline: Today.AddDays(-400), entitled: 30,
                status: EnumVacationAccrualStatus.Lapsed);
            accrual.UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var context = new LapseContext(accrual);

            var lapsed = await context.Service.SweepLapsedAsync();

            Assert.Equal(0, lapsed);
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), accrual.UpdatedAt);
            Assert.Equal(0, context.CommitCalls);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(365)]
        public async Task Sweep_LeavesAFutureDeadlineAlone(int daysAhead)
        {
            var accrual = Accrual(deadline: Today.AddDays(daysAhead), entitled: 30);
            var context = new LapseContext(accrual);

            Assert.Equal(0, await context.Service.SweepLapsedAsync());
            Assert.NotEqual((int)EnumVacationAccrualStatus.Lapsed, accrual.Status);
        }

        [Fact]
        public async Task Sweep_LeavesTheDeadlineDayItselfAlone()
        {
            // ConcessionDeadline is the last legal day to start the férias, not the first illegal
            // one. Lapsing on the day itself would invent a debt that is not owed yet.
            var accrual = Accrual(deadline: Today, entitled: 30);
            var context = new LapseContext(accrual);

            Assert.Equal(0, await context.Service.SweepLapsedAsync());
        }

        [Fact]
        public async Task Sweep_SweepsEveryTenant()
        {
            // Runs outside a request, where the ambient tenant filter is deliberately disabled. A
            // liability that only accrued for one tenant would be worse than none.
            var mine = Accrual(deadline: Today.AddDays(-5), entitled: 30, tenantId: TenantId);
            var theirs = Accrual(deadline: Today.AddDays(-5), entitled: 30, tenantId: OtherTenantId);
            var context = new LapseContext(mine, theirs);

            Assert.Equal(2, await context.Service.SweepLapsedAsync());
        }

        [Fact]
        public async Task Sweep_WhenOneRowCannotBeWritten_StillProcessesTheRest()
        {
            var doomed = Accrual(deadline: Today.AddDays(-10), entitled: 30);
            var healthy = Accrual(deadline: Today.AddDays(-5), entitled: 30);
            var context = new LapseContext(doomed, healthy);

            // The oldest deadline is swept first, so the throwing row is the first one reached.
            context.FailCommitFor(doomed.Id);

            var lapsed = await context.Service.SweepLapsedAsync();

            Assert.Equal(1, lapsed);
            Assert.Equal((int)EnumVacationAccrualStatus.Lapsed, healthy.Status);
        }

        [Fact]
        public async Task Sweep_HonoursTheBatchSize()
        {
            var accruals = Enumerable.Range(1, 5)
                .Select(day => Accrual(deadline: Today.AddDays(-day), entitled: 30))
                .ToArray();

            var context = new LapseContext(accruals) { BatchSize = 2 };

            Assert.Equal(2, await context.Service.SweepLapsedAsync());
        }

        [Fact]
        public async Task Sweep_SkipsAnInactiveAccrual()
        {
            var accrual = Accrual(deadline: Today.AddDays(-10), entitled: 30);
            accrual.IsActive = false;
            var context = new LapseContext(accrual);

            Assert.Equal(0, await context.Service.SweepLapsedAsync());
        }

        #endregion

        #region Approaching deadline

        [Fact]
        public async Task Approaching_ReturnsAccrualsInsideTheHorizonWithDaysLeft()
        {
            var soon = Accrual(deadline: Today.AddDays(10), entitled: 30, taken: 5);
            var later = Accrual(deadline: Today.AddDays(200), entitled: 30);
            var context = new LapseContext(soon, later);

            var approaching = await context.Service.GetApproachingDeadlineAsync(TenantId);

            Assert.Single(approaching);
            Assert.Equal(soon.Id, approaching[0].Id);
        }

        [Fact]
        public async Task Approaching_ExcludesAccrualsThatAlreadyLapsed()
        {
            // Past the deadline it is a debt to settle, not a deadline to meet. Listing both
            // together hides which is which.
            var context = new LapseContext(
                Accrual(deadline: Today.AddDays(-1), entitled: 30, status: EnumVacationAccrualStatus.Lapsed));

            Assert.Empty(await context.Service.GetApproachingDeadlineAsync(TenantId));
        }

        [Fact]
        public async Task Approaching_ExcludesAccrualsWithNothingOutstanding()
        {
            var context = new LapseContext(Accrual(deadline: Today.AddDays(5), entitled: 30, taken: 30));

            Assert.Empty(await context.Service.GetApproachingDeadlineAsync(TenantId));
        }

        [Fact]
        public async Task Approaching_IsScopedToTheCallersTenant()
        {
            var context = new LapseContext(
                Accrual(deadline: Today.AddDays(5), entitled: 30, tenantId: TenantId),
                Accrual(deadline: Today.AddDays(5), entitled: 30, tenantId: OtherTenantId));

            var approaching = await context.Service.GetApproachingDeadlineAsync(TenantId);

            Assert.Single(approaching);
            Assert.Equal(TenantId, approaching[0].EnterpriseId);
        }

        [Fact]
        public async Task Approaching_WithNoTenant_ReturnsNothing()
        {
            var context = new LapseContext(Accrual(deadline: Today.AddDays(5), entitled: 30));

            Assert.Empty(await context.Service.GetApproachingDeadlineAsync(Guid.Empty));
        }

        [Fact]
        public async Task Approaching_OrdersByTheNearestDeadline()
        {
            var far = Accrual(deadline: Today.AddDays(50), entitled: 30);
            var near = Accrual(deadline: Today.AddDays(2), entitled: 30);
            var context = new LapseContext(far, near);

            var approaching = await context.Service.GetApproachingDeadlineAsync(TenantId);

            Assert.Equal([near.Id, far.Id], approaching.Select(x => x.Id));
        }

        #endregion

        #region Worker

        [Fact]
        public async Task Worker_SkipsThePass_WhenAnotherReplicaHoldsTheLease()
        {
            var lease = new Mock<IDistributedLease>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((string?)null);

            var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
            var worker = new EmployeeVacationLapseWorker(
                scopeFactory.Object,
                Options.Create(new EmployeeVacationSettings()),
                lease.Object);

            var ran = await worker.RunOnceUnderLeaseAsync(TimeSpan.FromHours(12), CancellationToken.None);

            Assert.False(ran);

            // Strict mock: touching the scope factory at all would have thrown. Two replicas both
            // sweeping would log the same liability twice.
            scopeFactory.Verify(x => x.CreateScope(), Times.Never);
        }

        [Fact]
        public void Worker_UsesItsOwnLeaseKey()
        {
            var worker = new EmployeeVacationLapseWorker(
                Mock.Of<IServiceScopeFactory>(),
                Options.Create(new EmployeeVacationSettings()),
                Mock.Of<IDistributedLease>());

            // Sharing a key with the NFe sweeper would make each block the other.
            Assert.Equal("eva:lease:vacation-lapse", worker.LeaseKey);
            Assert.NotEqual("eva:lease:nfe-reconciliation", worker.LeaseKey);
        }

        [Fact]
        public void Worker_ClampsTheIntervalToAtLeastAMinute()
        {
            var worker = new EmployeeVacationLapseWorker(
                Mock.Of<IServiceScopeFactory>(),
                Options.Create(new EmployeeVacationSettings { LapseSweepIntervalMinutes = 0 }),
                Mock.Of<IDistributedLease>());

            Assert.Equal(TimeSpan.FromMinutes(1), worker.TimeUntilNextRun(DateTime.UtcNow));
        }

        #endregion

        private static EmployeeVacationAccrual Accrual(
            DateTime deadline,
            int entitled,
            int scheduled = 0,
            int taken = 0,
            int sold = 0,
            EnumVacationAccrualStatus status = EnumVacationAccrualStatus.Available,
            Guid? tenantId = null) => new()
            {
                Id = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                EnterpriseId = tenantId ?? TenantId,
                AcquisitionStart = deadline.AddMonths(-24),
                AcquisitionEnd = deadline.AddMonths(-12),
                ConcessionDeadline = deadline,
                EntitledDays = entitled,
                ScheduledDays = scheduled,
                TakenDays = taken,
                SoldDays = sold,
                Status = (int)status,
                IsActive = true
            };

        /// <summary>
        /// Runs the service's real predicate against an in-memory ledger. Mocking the filtered
        /// result instead would leave the predicate — which is the whole business rule — untested.
        /// </summary>
        private sealed class LapseContext
        {
            private readonly List<EmployeeVacationAccrual> _ledger;
            private Guid? _failCommitFor;

            public Mock<IRepository<EmployeeVacationAccrual>> Repository { get; } = new();
            public int CommitCalls { get; private set; }
            public int BatchSize { get; init; } = 200;

            public LapseContext(params EmployeeVacationAccrual[] accruals)
            {
                _ledger = [.. accruals];

                // The paged overload, because that is the one the sweeper calls: the batch cap has
                // to reach SQL rather than trimming a list already pulled into memory. Compiling
                // the service's real predicate against an in-memory ledger keeps the business rule
                // itself under test instead of a mocked-out answer.
                Repository
                    .Setup(x => x.GetAllAsync(
                        It.IsAny<int?>(),
                        It.IsAny<int?>(),
                        It.IsAny<Expression<Func<EmployeeVacationAccrual, bool>>>()))
                    .ReturnsAsync((int? pageNumber, int? pageSize, Expression<Func<EmployeeVacationAccrual, bool>>? predicate) =>
                    {
                        IEnumerable<EmployeeVacationAccrual> rows =
                            predicate == null ? _ledger : _ledger.Where(predicate.Compile());

                        if (pageNumber is > 0 && pageSize is > 0)
                            rows = rows.Skip((pageNumber.Value - 1) * pageSize.Value).Take(pageSize.Value);

                        return rows.ToList();
                    });

                // The unpaged overload too: GetApproachingDeadlineAsync is a request-path read with
                // no batch cap, so it calls the other one.
                Repository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<EmployeeVacationAccrual, bool>>>()))
                    .ReturnsAsync((Expression<Func<EmployeeVacationAccrual, bool>>? predicate) =>
                        predicate == null ? _ledger : _ledger.Where(predicate.Compile()).ToList());

                EmployeeVacationAccrual? pending = null;

                Repository
                    .Setup(x => x.Update(It.IsAny<EmployeeVacationAccrual>()))
                    .Returns((EmployeeVacationAccrual accrual) =>
                    {
                        pending = accrual;
                        return accrual;
                    });

                Repository
                    .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        if (_failCommitFor != null && pending?.Id == _failCommitFor)
                            throw new InvalidOperationException("Simulated write failure.");

                        CommitCalls++;
                        return Task.CompletedTask;
                    });
            }

            public void FailCommitFor(Guid accrualId) => _failCommitFor = accrualId;

            public EmployeeVacationLapseService Service => new(
                Repository.Object,
                Options.Create(new EmployeeVacationSettings { LapseSweepBatchSize = BatchSize }));
        }
    }
}
