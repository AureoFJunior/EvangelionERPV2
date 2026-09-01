using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.EmployeeModule.Test
{
    public class EmployeeVacationServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid EmployeeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [Fact]
        public async Task RefreshAsync_OpensOnePeriodPerCompletedYearOfTenure()
        {
            // Hired 30 months ago: two closed aquisitivos plus the one still running.
            var (sut, world) = BuildSut(hiredMonthsAgo: 30);

            var ledger = await sut.RefreshAsync(EmployeeId, EnterpriseId);

            Assert.Equal(3, ledger.Count);
            Assert.Equal(3, world.Accruals.Count);
            Assert.Equal((int)EnumVacationAccrualStatus.Available, ledger[0].Status);
            Assert.Equal((int)EnumVacationAccrualStatus.Accruing, ledger[^1].Status);
        }

        [Fact]
        public async Task RefreshAsync_IsIdempotent()
        {
            var (sut, world) = BuildSut(hiredMonthsAgo: 30);

            await sut.RefreshAsync(EmployeeId, EnterpriseId);
            var second = await sut.RefreshAsync(EmployeeId, EnterpriseId);

            // Matched on AcquisitionStart, so a second run reconciles the same three rows instead
            // of stacking three more onto the ledger.
            Assert.Equal(3, world.Accruals.Count);
            Assert.Equal(3, second.Count);
            Assert.All(second, accrual => Assert.Equal(30, accrual.EntitledDays));
        }

        [Fact]
        public async Task RefreshAsync_ComputesEntitlementFromTheFaltasInsideEachPeriod()
        {
            var (sut, world) = BuildSut(hiredMonthsAgo: 30);
            var hireDate = world.Employee.HireDate.Date;

            // Six unjustified days inside the first aquisitivo drops it a whole band, 30 -> 24.
            world.Absences.Add(Absence(EnumEmployeeAbsenceType.Falta, hireDate.AddDays(40), hireDate.AddDays(45), justified: false));

            var ledger = await sut.RefreshAsync(EmployeeId, EnterpriseId);

            Assert.Equal(24, ledger[0].EntitledDays);
            // The band is per period: the second one saw none of those faltas.
            Assert.Equal(30, ledger[1].EntitledDays);
        }

        [Fact]
        public async Task RefreshAsync_LeavesALapsedAccrualAlone()
        {
            var (sut, world) = BuildSut(hiredMonthsAgo: 30);
            await sut.RefreshAsync(EmployeeId, EnterpriseId);

            var lapsed = world.Accruals[0];
            lapsed.Status = (int)EnumVacationAccrualStatus.Lapsed;
            lapsed.EntitledDays = 30;

            // A dobro liability someone else already established. Recomputing it would erase the
            // evidence that the company owes those days twice.
            world.Absences.Add(Absence(EnumEmployeeAbsenceType.Falta, lapsed.AcquisitionStart.AddDays(10), lapsed.AcquisitionStart.AddDays(50), justified: false));

            await sut.RefreshAsync(EmployeeId, EnterpriseId);

            Assert.Equal((int)EnumVacationAccrualStatus.Lapsed, lapsed.Status);
            Assert.Equal(30, lapsed.EntitledDays);
        }

        [Fact]
        public async Task ScheduleAsync_WritesTheAbsenceRowAndTheDecrementTogether()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            await sut.ScheduleAsync(accrual.Id, EnterpriseId, accrual.AcquisitionEnd.AddDays(10), 20);

            Assert.Equal(20, accrual.ScheduledDays);
            Assert.Equal(10, accrual.RemainingDays);

            var booked = Assert.Single(world.Absences.Where(x => x.Type == (int)EnumEmployeeAbsenceType.Vacation));
            Assert.Equal(20, (booked.EndDate - booked.StartDate).Days + 1);

            // Paid leave: this row must never travel PayrollService.ReducesPay, which would cut the
            // salary a second time, nor VacationEntitlement's falta count.
            Assert.True(booked.IsJustified);
        }

        [Fact]
        public async Task RefreshAsync_AfterScheduling_KeepsTheBookingInsteadOfDriftingFromIt()
        {
            // 14 months of tenure: the first aquisitivo closed two months ago and its concessivo
            // window is still open, so leave booked into the future stays a reservation.
            var (sut, world) = await BuildScheduledSut(hiredMonthsAgo: 14);
            var accrual = world.Accruals[0];
            await sut.ScheduleAsync(accrual.Id, EnterpriseId, DateTime.UtcNow.Date.AddDays(30), 20);

            // Refresh recomputes the counters off the absence rows while ScheduleAsync incremented
            // them directly. The two derivations have to agree, or a routine read of the ledger
            // silently gives an employee back leave they have already booked.
            await sut.RefreshAsync(EmployeeId, EnterpriseId);

            Assert.Equal(20, accrual.ScheduledDays);
            Assert.Equal(0, accrual.TakenDays);
            Assert.Equal(10, accrual.RemainingDays);
            Assert.Equal((int)EnumVacationAccrualStatus.PartiallyTaken, accrual.Status);
        }

        [Fact]
        public async Task RefreshAsync_SettlesLeaveThatHasAlreadyElapsed()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            // Booked inside a concessivo window that has since closed: a reservation becomes
            // history, and the ledger says taken rather than still owed.
            world.Absences.Add(Absence(
                EnumEmployeeAbsenceType.Vacation,
                accrual.AcquisitionEnd.AddDays(5),
                accrual.AcquisitionEnd.AddDays(19),
                justified: true));

            await sut.RefreshAsync(EmployeeId, EnterpriseId);

            Assert.Equal(15, accrual.TakenDays);
            Assert.Equal(0, accrual.ScheduledDays);
            Assert.Equal(15, accrual.RemainingDays);
        }

        [Fact]
        public async Task ScheduleAsync_RejectsMoreDaysThanRemain()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.ScheduleAsync(accrual.Id, EnterpriseId, accrual.AcquisitionEnd.AddDays(10), 31));

            Assert.Contains("30 day(s) remain", exception.Message);
            Assert.Empty(world.Absences.Where(x => x.Type == (int)EnumEmployeeAbsenceType.Vacation));
        }

        [Fact]
        public async Task ScheduleAsync_RejectsAStartAfterTheConcessivoDeadline()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.ScheduleAsync(accrual.Id, EnterpriseId, accrual.ConcessionDeadline.AddDays(1), 20));

            Assert.Contains("concessivo", exception.Message);
        }

        [Fact]
        public async Task ScheduleAsync_RejectsAPeriodUnderFiveDays()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.ScheduleAsync(accrual.Id, EnterpriseId, accrual.AcquisitionEnd.AddDays(10), 4));

            Assert.Contains("art. 134", exception.Message);
        }

        [Fact]
        public async Task ScheduleAsync_RejectsAFourthPeriod()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];
            var window = accrual.AcquisitionEnd;

            await sut.ScheduleAsync(accrual.Id, EnterpriseId, window.AddDays(10), 14);
            await sut.ScheduleAsync(accrual.Id, EnterpriseId, window.AddDays(60), 8);
            await sut.ScheduleAsync(accrual.Id, EnterpriseId, window.AddDays(120), 5);

            // Three days still remain, but art. 134 §1 has run out of periods to put them in.
            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.ScheduleAsync(accrual.Id, EnterpriseId, window.AddDays(200), 3));

            Assert.Contains("art. 134", exception.Message);
        }

        [Fact]
        public async Task ScheduleAsync_RefusesWhileThePeriodIsStillAccruing()
        {
            var (sut, world) = BuildSut(hiredMonthsAgo: 30);
            var ledger = await sut.RefreshAsync(EmployeeId, EnterpriseId);
            var open = ledger[^1];

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.ScheduleAsync(open.Id, EnterpriseId, DateTime.UtcNow.Date.AddDays(30), 20));

            Assert.Contains("has not closed yet", exception.Message);
        }

        [Fact]
        public async Task SellDaysAsync_CapsTheAbonoAtAThirdOfTheEntitlement()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];
            var inTime = accrual.AcquisitionEnd.AddDays(-30);

            await sut.SellDaysAsync(accrual.Id, EnterpriseId, 10, inTime);
            Assert.Equal(10, accrual.SoldDays);

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.SellDaysAsync(accrual.Id, EnterpriseId, 1, inTime));

            Assert.Contains("art. 143", exception.Message);
            Assert.Equal(10, accrual.SoldDays);
        }

        [Fact]
        public async Task SellDaysAsync_RejectsARequestInsideTheFifteenDayDeadline()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.SellDaysAsync(accrual.Id, EnterpriseId, 5, accrual.AcquisitionEnd.AddDays(-14)));

            Assert.Contains("art. 143 §1", exception.Message);
            Assert.Equal(0, accrual.SoldDays);
        }

        [Fact]
        public async Task SellDaysAsync_WhenEveryFeriasIsBilledAndNoDaysRemain_RefusesTheSale()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];
            var inTime = accrual.AcquisitionEnd.AddDays(-30);
            var start = accrual.AcquisitionEnd.AddDays(10);

            // 30 entitled: the whole leave is booked in one stretch and that stretch is already
            // billed, so the 10 sellable days are the last thing left on the accrual.
            await sut.ScheduleAsync(accrual.Id, EnterpriseId, start, 20);
            world.PayableBills.Add(VacationBill(accrual, start.Date.AddDays(-2)));

            var exception = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.SellDaysAsync(accrual.Id, EnterpriseId, 10, inTime));

            // Nothing would ever issue a bill carrying them: GenerateAsync only hangs the abono on
            // a stretch it is billing on that run, and there is no unbilled stretch left nor any
            // day to schedule one with.
            Assert.Contains("no férias payment to be paid on", exception.Message);
            Assert.Equal(0, accrual.SoldDays);
        }

        [Fact]
        public async Task SellDaysAsync_WhenAScheduledFeriasIsStillUnbilled_AllowsTheSale()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            await sut.ScheduleAsync(accrual.Id, EnterpriseId, accrual.AcquisitionEnd.AddDays(10), 20);

            // Same shape as above minus the bill: the stretch has not been paid yet, so the days
            // sold now ride it when it is.
            await sut.SellDaysAsync(accrual.Id, EnterpriseId, 10, accrual.AcquisitionEnd.AddDays(-30));

            Assert.Equal(10, accrual.SoldDays);
        }

        [Fact]
        public async Task ScheduleAsync_RefusesAnAccrualFromAnotherTenant()
        {
            var (sut, world) = await BuildScheduledSut();
            var accrual = world.Accruals[0];

            await Assert.ThrowsAsync<NotFoundDatabaseException>(
                () => sut.ScheduleAsync(accrual.Id, Guid.NewGuid(), accrual.AcquisitionEnd.AddDays(10), 20));
        }

        private sealed class World
        {
            public required Employee Employee { get; init; }
            public List<EmployeeVacationAccrual> Accruals { get; } = [];
            public List<EmployeeAbsence> Absences { get; } = [];
            public List<PayableBill> PayableBills { get; } = [];
        }

        /// <summary>A ledger refreshed once, so the first period is closed and schedulable.</summary>
        private static async Task<(EmployeeVacationService sut, World world)> BuildScheduledSut(int hiredMonthsAgo = 30)
        {
            var (sut, world) = BuildSut(hiredMonthsAgo);
            await sut.RefreshAsync(EmployeeId, EnterpriseId);
            return (sut, world);
        }

        private static (EmployeeVacationService sut, World world) BuildSut(int hiredMonthsAgo)
        {
            var world = new World
            {
                Employee = new Employee
                {
                    Id = EmployeeId,
                    EnterpriseId = EnterpriseId,
                    HireDate = DateTime.UtcNow.Date.AddMonths(-hiredMonthsAgo),
                    IsActive = true
                }
            };

            var accruals = ListRepository(world.Accruals);
            var absences = ListRepository(world.Absences);
            var payableBills = ListRepository(world.PayableBills);

            var employees = new Mock<IRepository<Employee>>();
            employees.Setup(x => x.GetByIdAsync(EmployeeId)).ReturnsAsync(world.Employee);

            return (new EmployeeVacationService(
                accruals.Object, absences.Object, employees.Object, payableBills.Object), world);
        }

        /// <summary>
        /// An in-memory stand-in backed by a list, so predicates are evaluated for real rather than
        /// stubbed per call — the queries under test are the ones that decide entitlement.
        /// </summary>
        private static Mock<IRepository<TEntity>> ListRepository<TEntity>(List<TEntity> store)
            where TEntity : BaseEntity
        {
            var repository = new Mock<IRepository<TEntity>>();

            repository.Setup(x => x.CreateAsync(It.IsAny<TEntity>()))
                .ReturnsAsync((TEntity entity) =>
                {
                    store.Add(entity);
                    return entity;
                });

            repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => store.FirstOrDefault(x => x.Id == id)!);

            repository.Setup(x => x.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<TEntity, bool>>>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate) =>
                    predicate == null ? store.ToList() : store.Where(predicate.Compile()).ToList());

            repository.Setup(x => x.Update(It.IsAny<TEntity>()))
                .Returns((TEntity entity) => entity);

            repository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // The transaction wrapper runs the body inline: these tests are about the rules the
            // body enforces, not about the isolation level around it.
            repository.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());

            return repository;
        }

        /// <summary>A standing férias bill for the stretch falling due on <paramref name="dueDate"/>.</summary>
        private static PayableBill VacationBill(EmployeeVacationAccrual accrual, DateTime dueDate) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            BillType = (int)EnumPayableBillType.Vacation,
            EmployeeVacationAccrualId = accrual.Id,
            DueDate = dueDate,
            Amount = 2000m,
            IsActive = true
        };

        private static EmployeeAbsence Absence(EnumEmployeeAbsenceType type, DateTime start, DateTime end, bool justified) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = EmployeeId,
            EnterpriseId = EnterpriseId,
            Type = (int)type,
            StartDate = start.Date,
            EndDate = end.Date,
            IsJustified = justified,
            IsActive = true
        };
    }
}
