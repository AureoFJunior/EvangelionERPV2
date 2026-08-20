using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// The férias payment consumes the provision payroll accrued for it (spec §9 H1), so these
    /// tests are about which competências give up how much — and about the fact that running the
    /// calculation twice must not consume twice.
    /// </summary>
    public class VacationProvisionDrawdownServiceTests
    {
        private static readonly Guid EmployeeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid AccrualId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        #region Allocation order

        [Fact]
        public async Task Recalculate_ConsumesTheOldestCompetenciaFirst()
        {
            // 300 paid against three months of 200 each: the first is emptied, the second takes the
            // rest, the third is untouched.
            var context = new DrawdownContext(
                paid: 300m,
                competencias: [Month(2025, 1, 200m), Month(2025, 2, 200m), Month(2025, 3, 200m)]);

            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(200m, context.Consumed(2025, 1));
            Assert.Equal(100m, context.Consumed(2025, 2));
            Assert.Equal(0m, context.Consumed(2025, 3));
        }

        [Fact]
        public async Task Recalculate_IgnoresTheOrderRowsArriveIn()
        {
            // The repository makes no ordering promise, and "oldest first" must be a property of
            // the allocator rather than of however the rows happened to come back.
            var context = new DrawdownContext(
                paid: 150m,
                competencias: [Month(2025, 12, 200m), Month(2025, 3, 200m), Month(2025, 7, 200m)]);

            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(150m, context.Consumed(2025, 3));
            Assert.Equal(0m, context.Consumed(2025, 7));
            Assert.Equal(0m, context.Consumed(2025, 12));
        }

        [Fact]
        public async Task Recalculate_CrossesTheYearBoundaryInRealChronologicalOrder()
        {
            // A período aquisitivo straddles a new year, so December must come before January of
            // the following one — which a naive sort on month alone gets backwards.
            var context = new DrawdownContext(
                paid: 250m,
                competencias: [Month(2025, 12, 200m), Month(2026, 1, 200m)],
                acquisitionStart: new DateTime(2025, 7, 1),
                acquisitionEnd: new DateTime(2026, 6, 30));

            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(200m, context.Consumed(2025, 12));
            Assert.Equal(50m, context.Consumed(2026, 1));
        }

        #endregion

        #region Partial and excess payments

        [Fact]
        public async Task Recalculate_WhenThePaymentIsSmallerThanTheProvision_LeavesTheRemainder()
        {
            // A split férias paying part of the entitlement: the rest stays available for the next
            // stretch rather than being written off.
            var context = new DrawdownContext(paid: 120m, competencias: [Month(2025, 1, 200m)]);

            var drawdown = await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(120m, context.Consumed(2025, 1));
            Assert.Equal(0m, drawdown.Shortfall);
            Assert.Equal(80m, context.Remaining(2025, 1));
        }

        [Fact]
        public async Task Recalculate_WhenThePaymentExceedsEverythingAccrued_ConsumesAllAndReportsTheShortfall()
        {
            // Ordinary for a mid-period hire or a raise since the provision was laid down. The cash
            // is owed either way, so this must not throw.
            var context = new DrawdownContext(
                paid: 500m,
                competencias: [Month(2025, 1, 200m), Month(2025, 2, 100m)]);

            var drawdown = await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(200m, context.Consumed(2025, 1));
            Assert.Equal(100m, context.Consumed(2025, 2));
            Assert.Equal(300m, drawdown.TotalConsumed);
            Assert.Equal(200m, drawdown.Shortfall);
        }

        [Fact]
        public async Task Recalculate_WithNoCompetenciasAtAll_ReportsTheWholePaymentAsShortfall()
        {
            var context = new DrawdownContext(paid: 500m, competencias: []);

            var drawdown = await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(500m, drawdown.Shortfall);
            Assert.Equal(0m, drawdown.TotalConsumed);
        }

        #endregion

        #region Idempotency and reversal

        [Fact]
        public async Task Recalculate_RunTwice_ConsumesNothingFurther()
        {
            // The whole idempotency story: the allocation is recomputed from the bills rather than
            // applied as a delta, so a second run lands on the same numbers with nothing to write.
            var context = new DrawdownContext(
                paid: 300m,
                competencias: [Month(2025, 1, 200m), Month(2025, 2, 200m)]);

            await context.Service.RecalculateAsync(AccrualId);
            var commitsAfterFirst = context.CommitCalls;

            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(200m, context.Consumed(2025, 1));
            Assert.Equal(100m, context.Consumed(2025, 2));
            Assert.Equal(commitsAfterFirst, context.CommitCalls);
        }

        [Fact]
        public async Task Recalculate_AfterASecondBill_ConsumesTheCombinedTotal()
        {
            var context = new DrawdownContext(
                paid: 150m,
                competencias: [Month(2025, 1, 200m), Month(2025, 2, 200m)]);

            await context.Service.RecalculateAsync(AccrualId);
            Assert.Equal(150m, context.Consumed(2025, 1));

            context.AddBill(200m);
            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(200m, context.Consumed(2025, 1));
            Assert.Equal(150m, context.Consumed(2025, 2));
        }

        [Fact]
        public async Task Recalculate_AfterTheBillIsRefunded_ReleasesTheProvisionItHeld()
        {
            var context = new DrawdownContext(paid: 150m, competencias: [Month(2025, 1, 200m)]);

            await context.Service.RecalculateAsync(AccrualId);
            Assert.Equal(150m, context.Consumed(2025, 1));

            context.RefundAllBills();
            await context.Service.RecalculateAsync(AccrualId);

            // Reversing the payment gives the months their provision back. Recomputation is what
            // makes this fall out for free instead of needing a compensating write.
            Assert.Equal(0m, context.Consumed(2025, 1));
        }

        #endregion

        #region Which rows are eligible

        [Fact]
        public async Task Recalculate_SkipsCompetenciasOutsideThePeriodoAquisitivo()
        {
            // The months the entitlement was earned in, not the months the leave is taken in.
            var context = new DrawdownContext(
                paid: 1000m,
                competencias:
                [
                    Month(2024, 12, 200m),  // before AcquisitionStart
                    Month(2025, 1, 200m),
                    Month(2026, 2, 200m)    // after AcquisitionEnd
                ]);

            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(0m, context.Consumed(2024, 12));
            Assert.Equal(200m, context.Consumed(2025, 1));
            Assert.Equal(0m, context.Consumed(2026, 2));
        }

        [Fact]
        public async Task Recalculate_IgnoresBillsThatAreNotFeriasBills()
        {
            var context = new DrawdownContext(paid: 100m, competencias: [Month(2025, 1, 200m)]);
            context.AddBill(500m, billType: EnumPayableBillType.Payroll);

            await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(100m, context.Consumed(2025, 1));
        }

        [Fact]
        public async Task Recalculate_IgnoresAnotherEmployeesPayroll()
        {
            var context = new DrawdownContext(paid: 500m, competencias: [Month(2025, 1, 200m)]);
            context.AddForeignCompetencia(Month(2025, 2, 200m));

            var drawdown = await context.Service.RecalculateAsync(AccrualId);

            Assert.Equal(200m, drawdown.TotalConsumed);
            Assert.Equal(300m, drawdown.Shortfall);
        }

        [Fact]
        public async Task Recalculate_WhenTheAccrualDoesNotExist_Throws()
        {
            var context = new DrawdownContext(paid: 100m, competencias: [], accrualExists: false);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Service.RecalculateAsync(AccrualId));
        }

        #endregion

        #region Harness

        private static PayrollBillDetail Month(int year, int month, decimal provision) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = EmployeeId,
            ReferenceYear = year,
            ReferenceMonth = month,
            VacationProvision = provision,
            IsActive = true
        };

        /// <summary>
        /// Compiles the service's real predicates against in-memory lists, so the eligibility rules
        /// under test are the ones the service actually ships rather than a mocked-out answer.
        /// </summary>
        private sealed class DrawdownContext
        {
            private readonly List<PayrollBillDetail> _details = [];
            private readonly List<PayableBill> _bills = [];

            public VacationProvisionDrawdownService Service { get; }
            public int CommitCalls { get; private set; }

            public DrawdownContext(
                decimal paid,
                IReadOnlyList<PayrollBillDetail> competencias,
                bool accrualExists = true,
                DateTime? acquisitionStart = null,
                DateTime? acquisitionEnd = null)
            {
                _details.AddRange(competencias);

                // A twelve-month período aquisitivo through calendar 2025, so the boundary tests
                // have somewhere obvious to fall outside of.
                var accrual = new EmployeeVacationAccrual
                {
                    Id = AccrualId,
                    EmployeeId = EmployeeId,
                    AcquisitionStart = acquisitionStart ?? new DateTime(2025, 1, 1),
                    AcquisitionEnd = acquisitionEnd ?? new DateTime(2025, 12, 31),
                    ConcessionDeadline = new DateTime(2026, 12, 31),
                    EntitledDays = 30,
                    IsActive = true
                };

                var accrualRepo = new Mock<IRepository<EmployeeVacationAccrual>>();
                accrualRepo.Setup(x => x.GetByIdAsync(AccrualId))
                    .ReturnsAsync(accrualExists ? accrual : null!);

                if (paid > 0)
                    AddBill(paid);

                var billRepo = new Mock<IRepository<PayableBill>>();
                billRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>?>()))
                    .ReturnsAsync((Expression<Func<PayableBill, bool>>? predicate) =>
                        predicate == null ? _bills.ToList() : _bills.Where(predicate.Compile()).ToList());

                var detailRepo = new Mock<IRepository<PayrollBillDetail>>();
                detailRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayrollBillDetail, bool>>?>()))
                    .ReturnsAsync((Expression<Func<PayrollBillDetail, bool>>? predicate) =>
                        predicate == null ? _details.ToList() : _details.Where(predicate.Compile()).ToList());
                detailRepo.Setup(x => x.UpdateRange(It.IsAny<IEnumerable<PayrollBillDetail>>()))
                    .Returns((IEnumerable<PayrollBillDetail> rows) => rows);
                detailRepo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        CommitCalls++;
                        return Task.CompletedTask;
                    });

                Service = new VacationProvisionDrawdownService(
                    accrualRepo.Object, billRepo.Object, detailRepo.Object);
            }

            public void AddBill(decimal amount, EnumPayableBillType billType = EnumPayableBillType.Vacation) =>
                _bills.Add(new PayableBill
                {
                    Id = Guid.NewGuid(),
                    Amount = amount,
                    BillType = (int)billType,
                    EmployeeVacationAccrualId = AccrualId,
                    IsActive = true
                });

            public void RefundAllBills()
            {
                foreach (var bill in _bills)
                    bill.RefundedAt = DateTime.UtcNow;
            }

            /// <summary>A competência belonging to somebody else, which must never be drawn on.</summary>
            public void AddForeignCompetencia(PayrollBillDetail detail)
            {
                detail.EmployeeId = Guid.NewGuid();
                _details.Add(detail);
            }

            public decimal Consumed(int year, int month) => Detail(year, month).VacationProvisionConsumed;

            public decimal Remaining(int year, int month)
            {
                var detail = Detail(year, month);
                return detail.VacationProvision - detail.VacationProvisionConsumed;
            }

            private PayrollBillDetail Detail(int year, int month) =>
                _details.Single(x => x.ReferenceYear == year && x.ReferenceMonth == month && x.EmployeeId == EmployeeId);
        }

        #endregion
    }
}
