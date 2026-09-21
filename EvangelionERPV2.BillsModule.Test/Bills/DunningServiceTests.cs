using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class DunningServiceTests
    {
        [Fact]
        public async Task Sweep_ShouldSendOnceForAnOverdueReceivable_AndNeverAgainOnASecondPass()
        {
            var world = new World();
            var order = world.AddOverdueOrder(daysOverdue: 7, amount: 250);

            var first = await world.Service.SweepAsync();
            Assert.Equal(1, first);
            Assert.Single(world.SentEmails);
            Assert.Single(world.Logs);
            Assert.Equal(order.Id, world.Logs[0].OrderId);
            Assert.Equal(7, world.Logs[0].OffsetDays);

            var second = await world.Service.SweepAsync();
            Assert.Equal(0, second);
            Assert.Single(world.SentEmails);
        }

        [Fact]
        public async Task Sweep_ShouldOnlyFireOnDaysTheLadderNames()
        {
            foreach (var daysOverdue in new[] { 1, 6, 8, 14 })
            {
                var world = new World();
                world.AddOverdueOrder(daysOverdue, amount: 100);

                Assert.Equal(0, await world.Service.SweepAsync());
            }

            foreach (var daysOverdue in new[] { -3, 0, 7, 15, 30 })
            {
                var world = new World();
                world.AddOverdueOrder(daysOverdue, amount: 100);

                Assert.Equal(1, await world.Service.SweepAsync());
            }
        }

        [Fact]
        public async Task Sweep_ShouldRespectTheTenantOptIn()
        {
            var world = new World();
            world.Enterprise.ShouldSendDunning = false;
            world.AddOverdueOrder(daysOverdue: 7, amount: 100);

            Assert.Equal(0, await world.Service.SweepAsync());
            Assert.Empty(world.SentEmails);
        }

        [Fact]
        public async Task Sweep_ShouldStayQuietWhileTheFeatureIsOff()
        {
            var world = new World(new DunningSettings { Enabled = false });
            world.AddOverdueOrder(daysOverdue: 7, amount: 100);

            Assert.Equal(0, await world.Service.SweepAsync());
            Assert.Empty(world.Logs);
        }

        [Fact]
        public async Task Sweep_ShouldNotChaseASettledOrRefundedReceivable()
        {
            var paidWorld = new World();
            var paidOrder = paidWorld.AddOverdueOrder(daysOverdue: 7, amount: 100);
            var bill = paidWorld.AddBillFor(paidOrder, daysOverdue: 7);
            bill.Status = BillStatus.Paid;
            bill.PaidAt = DateTime.UtcNow.Date.AddDays(-6);

            Assert.Equal(0, await paidWorld.Service.SweepAsync());

            var refundedWorld = new World();
            var refunded = refundedWorld.AddOverdueOrder(daysOverdue: 7, amount: 100);
            refunded.RefundedAt = DateTime.UtcNow.Date.AddDays(-1);

            Assert.Equal(0, await refundedWorld.Service.SweepAsync());
        }

        [Fact]
        public async Task Sweep_ShouldSkipACustomerWithNoEmail_WithoutBurningTheLadderRung()
        {
            var world = new World();
            var order = world.AddOverdueOrder(daysOverdue: 7, amount: 100);
            world.Customers.Single(x => x.Id == order.CustomerId).Email = "   ";

            Assert.Equal(0, await world.Service.SweepAsync());
            Assert.Empty(world.Logs);
        }

        [Fact]
        public async Task Sweep_ShouldQuoteTheAmountAndDueDate_AndPromiseNoLateFee()
        {
            var world = new World();
            world.AddOverdueOrder(daysOverdue: 7, amount: 1234.56m);

            await world.Service.SweepAsync();

            var body = world.SentEmails.Single().Body;
            Assert.Contains($"{1234.56m:N2}", body);
            Assert.Contains($"{DateTime.UtcNow.Date.AddDays(-7):dd/MM/yyyy}", body);
            Assert.DoesNotContain("juros", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("multa", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Sweep_ShouldChaseAnOrderThatHasNoBoleto()
        {
            var world = new World();
            world.AddOverdueOrder(daysOverdue: 15, amount: 500);

            Assert.Equal(1, await world.Service.SweepAsync());
        }

        private sealed class World
        {
            public Enterprise Enterprise { get; }
            public List<Order> Orders { get; } = [];
            public List<Bill> Bills { get; } = [];
            public List<Customer> Customers { get; } = [];
            public List<DunningLog> Logs { get; } = [];
            public List<EmailStructure> SentEmails { get; } = [];
            public DunningService Service { get; }

            public World(DunningSettings? settings = null)
            {
                Enterprise = new Enterprise
                {
                    Id = Guid.NewGuid(),
                    Name = "NERV",
                    IsActive = true,
                    ShouldSendDunning = true
                };

                var emailService = new Mock<IEmailService<EmailStructure>>();
                emailService
                    .Setup(x => x.SendManualEmail(It.IsAny<EmailStructure>(), It.IsAny<Enterprise>()))
                    .Callback((EmailStructure email, Enterprise _) => SentEmails.Add(email))
                    .Returns(Task.CompletedTask);

                Service = new DunningService(
                    Repo([Enterprise]).Object,
                    Repo(Orders).Object,
                    Repo(Bills).Object,
                    Repo(Customers).Object,
                    LogRepo().Object,
                    () => emailService.Object,
                    Options.Create(settings ?? new DunningSettings { Enabled = true }));
            }

            public Order AddOverdueOrder(int daysOverdue, decimal amount)
            {
                var customer = new Customer
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = Enterprise.Id,
                    IsActive = true,
                    Name = "Shinji",
                    Email = "shinji@nerv.jp"
                };

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = Enterprise.Id,
                    IsActive = true,
                    TotalValue = amount,
                    Status = (int)EnumOrderStatus.Pending,
                    CustomerId = customer.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-60),
                    PaymentScheduledDate = DateTime.UtcNow.Date.AddDays(-daysOverdue)
                };

                Customers.Add(customer);
                Orders.Add(order);
                return order;
            }

            public Bill AddBillFor(Order order, int daysOverdue)
            {
                var bill = new Bill
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Order = order,
                    IsActive = true,
                    Amount = order.TotalValue,
                    DueDate = DateTime.UtcNow.Date.AddDays(-daysOverdue),
                    DocumentNumber = "000001",
                    DigitableLine = "34191790010104351004791020150008291070026000",
                    Status = BillStatus.Registered,
                    CreatedAt = DateTime.UtcNow.AddDays(-30)
                };

                Bills.Add(bill);
                return bill;
            }

            private Mock<IRepository<DunningLog>> LogRepo()
            {
                var repo = Repo(Logs);

                repo.Setup(x => x.CreateAsync(It.IsAny<DunningLog>()))
                    .ReturnsAsync((DunningLog log) =>
                    {
                        if (Logs.Any(x => x.EnterpriseId == log.EnterpriseId
                                          && x.OrderId == log.OrderId
                                          && x.OffsetDays == log.OffsetDays))
                        {
                            throw new InvalidOperationException("duplicate dunning log");
                        }

                        Logs.Add(log);
                        return log;
                    });

                repo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                return repo;
            }

            private static Mock<IRepository<TEntity>> Repo<TEntity>(List<TEntity> records)
                where TEntity : BaseEntity
            {
                var repo = new Mock<IRepository<TEntity>>();

                repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                    .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) => predicate == null
                        ? records
                        : records.Where(predicate.Compile()).ToList());

                repo.Setup(x => x.GetPagedAsync(
                        It.IsAny<Expression<Func<TEntity, bool>>?>(),
                        It.IsAny<Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?>(),
                        It.IsAny<int?>(),
                        It.IsAny<int?>()))
                    .ReturnsAsync((
                        Expression<Func<TEntity, bool>>? predicate,
                        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? _,
                        int? __,
                        int? ___) =>
                    {
                        var items = predicate == null
                            ? records.ToList()
                            : records.Where(predicate.Compile()).ToList();

                        return ((IReadOnlyList<TEntity>)items, items.Count);
                    });

                return repo;
            }
        }
    }
}
