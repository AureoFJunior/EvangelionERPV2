using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// Contas fixas: one link per pass, and never two for the same parent.
    /// </summary>
    public class RecurringPayableServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();

        [Fact]
        public async Task Sweep_ClonesTheBillOnePeriodForward()
        {
            var parent = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            var (service, bills, _) = BuildSut([parent]);

            Assert.Equal(1, await service.SweepAsync());

            var child = Assert.Single(bills, x => x.RecurrenceParentId == parent.Id);
            Assert.Equal(parent.DueDate.AddMonths(1), child.DueDate);
            Assert.Equal(parent.Amount, child.Amount);
            Assert.Equal(parent.Description, child.Description);
            Assert.Equal(parent.BillType, child.BillType);
            Assert.False(child.IsPaid);

            // The child carries the recurrence forward: it becomes the next parent in its turn.
            Assert.Equal(parent.RecurrenceMonths, child.RecurrenceMonths);
            Assert.Equal(parent.RecurrenceUntilUtc, child.RecurrenceUntilUtc);
        }

        [Fact]
        public async Task Sweep_TwiceInOneDay_GeneratesOneBill()
        {
            // The filtered unique index on RecurrenceParentId is the idempotency; the pre-check
            // just makes it graceful.
            var parent = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            var (service, bills, _) = BuildSut([parent]);

            Assert.Equal(1, await service.SweepAsync());
            Assert.Equal(0, await service.SweepAsync());

            Assert.Single(bills, x => x.RecurrenceParentId == parent.Id);
        }

        [Fact]
        public async Task Sweep_LeavesABillThatIsNotYetDue()
        {
            var parent = BuildRecurring(dueDate: Today.AddDays(5), months: 1);
            var (service, _, _) = BuildSut([parent]);

            Assert.Equal(0, await service.SweepAsync());
        }

        [Fact]
        public async Task Sweep_StopsAtTheEndDate()
        {
            var parent = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            // The next link would fall a month past the end of the chain.
            parent.RecurrenceUntilUtc = parent.DueDate.AddDays(3);

            var (service, _, _) = BuildSut([parent]);

            Assert.Equal(0, await service.SweepAsync());
        }

        [Fact]
        public async Task Sweep_IgnoresARefundedOrDeletedParent()
        {
            var refunded = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            refunded.RefundedAt = Today.AddDays(-1);

            var deleted = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            deleted.IsActive = false;

            var (service, _, _) = BuildSut([refunded, deleted]);

            Assert.Equal(0, await service.SweepAsync());
        }

        [Fact]
        public async Task Sweep_IgnoresABillThatDoesNotRepeat()
        {
            var once = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            once.RecurrenceMonths = null;
            once.RecurrenceUntilUtc = null;

            var (service, _, _) = BuildSut([once]);

            Assert.Equal(0, await service.SweepAsync());
        }

        [Fact]
        public async Task Sweep_NeverGeneratesIntoAClosedPeriod()
        {
            // The interaction that matters: a chain must not put a bill inside a month the
            // accountant already received.
            var parent = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            var (service, bills, _) = BuildSut([parent], closedThroughUtc: parent.DueDate.AddMonths(2));

            Assert.Equal(0, await service.SweepAsync());
            Assert.DoesNotContain(bills, x => x.RecurrenceParentId == parent.Id);
        }

        [Fact]
        public async Task Sweep_KeepsGoingWhenOneChainFails()
        {
            var broken = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            var healthy = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);

            var (service, bills, payableBillService) = BuildSut([broken, healthy]);
            payableBillService
                .Setup(x => x.CreateAsync(
                    It.Is<PayableBill>(bill => bill.RecurrenceParentId == broken.Id),
                    It.IsAny<bool>(),
                    It.IsAny<bool>()))
                .ThrowsAsync(new InsertDatabaseException("Unexpected error while creating payable bill."));

            Assert.Equal(1, await service.SweepAsync());
            Assert.Single(bills, x => x.RecurrenceParentId == healthy.Id);
        }

        [Fact]
        public async Task Sweep_DoesNotCarryItemLinesForward()
        {
            // Rent and internet deliver nothing; cloned lines would tell the stock ledger a
            // delivery is pending every month.
            var parent = BuildRecurring(dueDate: Today.AddDays(-1), months: 1);
            parent.Items = [new PayableBillProduct { ProductId = Guid.NewGuid(), Quantity = 1, UnitValue = 10 }];

            var (service, bills, _) = BuildSut([parent]);

            await service.SweepAsync();

            var child = Assert.Single(bills, x => x.RecurrenceParentId == parent.Id);
            Assert.Null(child.Items);
        }

        private static DateTime Today => DateTime.UtcNow.Date;

        private static PayableBill BuildRecurring(DateTime dueDate, int months) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            Description = "Aluguel",
            Amount = 4500,
            BillType = (int)EnumPayableBillType.OperatingCost,
            DueDate = dueDate,
            IsActive = true,
            RecurrenceMonths = months,
            RecurrenceUntilUtc = dueDate.AddYears(1)
        };

        private static (RecurringPayableService Service, List<PayableBill> Bills, Mock<IPayableBillService> PayableBillService)
            BuildSut(List<PayableBill> bills, DateTime? closedThroughUtc = null)
        {
            var store = bills.ToList();

            var payableBillRepository = new Mock<IRepository<PayableBill>>();
            payableBillRepository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>?>()))
                .ReturnsAsync((Expression<Func<PayableBill, bool>>? predicate) => predicate == null
                    ? store.ToList()
                    : store.Where(predicate.Compile()).ToList());

            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => new Enterprise
                {
                    Id = id,
                    IsActive = true,
                    Name = "Nerv",
                    ClosedThroughUtc = closedThroughUtc
                });

            var payableBillService = new Mock<IPayableBillService>();
            payableBillService
                .Setup(x => x.CreateAsync(It.IsAny<PayableBill>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync((PayableBill bill, bool _, bool __) =>
                {
                    bill.Id = bill.Id == Guid.Empty ? Guid.NewGuid() : bill.Id;
                    bill.IsActive = true;
                    store.Add(bill);
                    return bill;
                });

            var service = new RecurringPayableService(
                payableBillRepository.Object,
                enterpriseRepository.Object,
                payableBillService.Object);

            return (service, store, payableBillService);
        }
    }
}
