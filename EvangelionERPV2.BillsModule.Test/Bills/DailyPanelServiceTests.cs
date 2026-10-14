using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// The panel's boundaries are the whole test surface: a day either side of today decides
    /// which number a person sees first thing in the morning.
    /// </summary>
    public class DailyPanelServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();

        [Fact]
        public async Task GetToday_ShouldCountBillDueToday_AsDueToday_NotOverdue()
        {
            var today = DateTime.UtcNow.Date;
            var (order, bill) = BuildOrderWithBill(amount: 250, dueDate: today);

            var panel = await BuildSut(bills: [bill]).GetTodayAsync(EnterpriseId);

            Assert.Equal(1, panel.Receivables.DueTodayCount);
            Assert.Equal(250, panel.Receivables.DueTodayAmount);
            Assert.Equal(0, panel.Receivables.OverdueCount);
            Assert.Equal(1, panel.Receivables.OpenCount);
        }

        [Fact]
        public async Task GetToday_ShouldCountBillDueYesterday_AsOverdue()
        {
            var today = DateTime.UtcNow.Date;
            var (order, bill) = BuildOrderWithBill(amount: 400, dueDate: today.AddDays(-1));

            var panel = await BuildSut(bills: [bill]).GetTodayAsync(EnterpriseId);

            Assert.Equal(1, panel.Receivables.OverdueCount);
            Assert.Equal(400, panel.Receivables.OverdueAmount);
            Assert.Equal(0, panel.Receivables.DueTodayCount);
        }

        [Theory]
        [InlineData(BillStatus.Paid)]
        [InlineData(BillStatus.Cancelled)]
        public async Task GetToday_ShouldCountSettledAndCancelledTitles_InNeitherBucket(BillStatus status)
        {
            var today = DateTime.UtcNow.Date;
            var (order, bill) = BuildOrderWithBill(amount: 900, dueDate: today.AddDays(-3));
            bill.Status = status;
            bill.PaidAt = status == BillStatus.Paid ? today.AddDays(-3) : null;

            var panel = await BuildSut(bills: [bill]).GetTodayAsync(EnterpriseId);

            Assert.Equal(0, panel.Receivables.OpenCount);
            Assert.Equal(0, panel.Receivables.OverdueCount);
            Assert.Equal(0, panel.Receivables.DueTodayCount);
        }

        [Fact]
        public async Task GetToday_ShouldIgnoreTitlesOfRefundedOrders()
        {
            var today = DateTime.UtcNow.Date;
            var (order, bill) = BuildOrderWithBill(amount: 120, dueDate: today.AddDays(-10));
            order.RefundedAt = today.AddDays(-2);

            var panel = await BuildSut(bills: [bill]).GetTodayAsync(EnterpriseId);

            Assert.Equal(0, panel.Receivables.OpenCount);
        }

        [Fact]
        public async Task GetToday_ShouldSplitPayables_ByDueDate_WithinTheWeek()
        {
            var today = DateTime.UtcNow.Date;

            var panel = await BuildSut(payables:
            [
                BuildPayable(100, today.AddDays(-2)),
                BuildPayable(200, today),
                BuildPayable(300, today.AddDays(6)),
                BuildPayable(400, today.AddDays(30))
            ]).GetTodayAsync(EnterpriseId);

            Assert.Equal(1, panel.Payables.OverdueCount);
            Assert.Equal(100, panel.Payables.OverdueAmount);
            Assert.Equal(1, panel.Payables.DueTodayCount);
            Assert.Equal(200, panel.Payables.DueTodayAmount);
            Assert.Equal(3, panel.Payables.DueThisWeekCount);
            Assert.Equal(600, panel.Payables.DueThisWeekAmount);
        }

        [Fact]
        public async Task GetToday_WithoutCertificate_ShouldReportNotConfigured_NotExpired()
        {
            var panel = await BuildSut().GetTodayAsync(EnterpriseId);

            Assert.False(panel.Fiscal.CertificateConfigured);
            Assert.Null(panel.Fiscal.CertificateDaysRemaining);
        }

        [Fact]
        public async Task GetToday_WithCertificate_ShouldReportDaysRemaining()
        {
            var settings = new EnterpriseFiscalSettings
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                CertNotAfterUtc = DateTime.UtcNow.Date.AddDays(12)
            };

            var panel = await BuildSut(fiscalSettings: [settings]).GetTodayAsync(EnterpriseId);

            Assert.True(panel.Fiscal.CertificateConfigured);
            Assert.Equal(12, panel.Fiscal.CertificateDaysRemaining);
        }

        [Fact]
        public async Task GetToday_ShouldCountTransmittedPendingDocuments_AsOutcomeUnknown()
        {
            var stuck = new NFeDocument
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                Status = NFeStatus.Pending,
                TransmittedAtUtc = DateTime.UtcNow.AddHours(-3)
            };

            var queued = new NFeDocument
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                Status = NFeStatus.Pending
            };

            var panel = await BuildSut(nfeDocuments: [stuck, queued]).GetTodayAsync(EnterpriseId);

            Assert.Equal(2, panel.Fiscal.PendingCount);
            Assert.Equal(1, panel.Fiscal.OutcomeUnknownCount);
            Assert.Equal(0, panel.Fiscal.RejectedCount);
        }

        [Fact]
        public async Task GetToday_ShouldCountUnmatchedAndAmbiguousBankLines_ButNotIgnoredOnes()
        {
            var lines = new List<BankStatementLine>
            {
                BuildBankLine(BankStatementLineStatus.Unmatched),
                BuildBankLine(BankStatementLineStatus.Ambiguous),
                BuildBankLine(BankStatementLineStatus.Ignored),
                BuildBankLine(BankStatementLineStatus.Matched)
            };

            var panel = await BuildSut(bankLines: lines).GetTodayAsync(EnterpriseId);

            Assert.Equal(2, panel.Bank.UnclearedCount);
        }

        [Fact]
        public async Task GetToday_ShouldCountEveryCriticalProduct_ButReturnOnlyTheFiveNames()
        {
            var suggestions = Enumerable.Range(1, 7)
                .Select(index => new ReplenishmentSuggestionDTO
                {
                    ProductName = $"Product {index}",
                    Criticality = "high"
                })
                .ToList();

            var panel = await BuildSut(replenishmentSuggestions: suggestions).GetTodayAsync(EnterpriseId);

            Assert.Equal(7, panel.Stock.CriticalCount);
            Assert.Equal(5, panel.Stock.CriticalProducts.Count);
        }

        private static (Order order, Bill bill) BuildOrderWithBill(decimal amount, DateTime dueDate)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                TotalValue = amount
            };

            var bill = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Order = order,
                IsActive = true,
                Amount = amount,
                DueDate = dueDate,
                Status = BillStatus.Registered,
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            };

            return (order, bill);
        }

        private static PayableBill BuildPayable(decimal amount, DateTime dueDate) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Amount = amount,
            DueDate = dueDate
        };

        private static BankStatementLine BuildBankLine(BankStatementLineStatus status) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Status = (int)status
        };

        private static DailyPanelService BuildSut(
            List<Bill>? bills = null,
            List<PayableBill>? payables = null,
            List<NFeDocument>? nfeDocuments = null,
            List<EnterpriseFiscalSettings>? fiscalSettings = null,
            List<BankStatementLine>? bankLines = null,
            List<ReplenishmentSuggestionDTO>? replenishmentSuggestions = null)
        {
            var payableBillService = new Mock<IPayableBillService>();
            payableBillService
                .Setup(x => x.GetReplenishmentSuggestionsAsync(It.IsAny<Guid>(), It.IsAny<ReplenishmentSuggestionRequestDTO>()))
                .ReturnsAsync(replenishmentSuggestions ?? []);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"daily-panel-tests-{Guid.NewGuid():N}")
                .Options;

            var context = new AppDbContext(options, httpContextAccessor: null, auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
            context.Database.EnsureCreated();

            foreach (var bill in bills ?? [])
                context.Set<Bill>().Add(bill);
            foreach (var payable in payables ?? [])
                context.Set<PayableBill>().Add(payable);

            context.SaveChanges();

            return new DailyPanelService(
                context,
                Repo(nfeDocuments ?? []).Object,
                Repo(fiscalSettings ?? []).Object,
                Repo(bankLines ?? []).Object,
                payableBillService.Object,
                new NullDistributedCache());
        }

        private sealed class NoopAuditTrailEntryFactory : IAuditTrailEntryFactory
        {
            public IReadOnlyCollection<AuditTrail> Create(
                IEnumerable<EntityEntry<BaseEntity>> entries,
                Guid? userId,
                Guid? enterpriseId,
                DateTime changedAt)
            {
                return Array.Empty<AuditTrail>();
            }
        }

        private static Mock<IRepository<TEntity>> Repo<TEntity>(List<TEntity> records)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) => predicate == null
                    ? records
                    : records.Where(predicate.Compile()).ToList());

            return repo;
        }

        /// <summary>A cache that never has anything, so every test measures the real query.</summary>
        private sealed class NullDistributedCache : IDistributedCache
        {
            public byte[]? Get(string key) => null;
            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
            public void Refresh(string key) { }
            public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
            public void Remove(string key) { }
            public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
            public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
        }
    }
}
