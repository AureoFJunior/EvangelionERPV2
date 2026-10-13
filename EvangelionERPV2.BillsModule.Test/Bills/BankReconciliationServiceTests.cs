using System.Linq.Expressions;
using System.Text;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class BankReconciliationServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [Fact]
        public async Task ImportAsync_StoresTheStatementLinesAndTheClosingBalance()
        {
            var harness = new Harness();

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "10250.10",
                transactions: [Transaction("F1", "20260805", "1500.00"), Transaction("F2", "20260807", "-249.90")]));

            Assert.Equal(2, result.TotalLines);
            Assert.Equal(2, result.NewLines);
            Assert.Equal(10250.10m, result.LedgerBalance);
            Assert.Equal(2, harness.Lines.Count);
            Assert.All(harness.Lines, line => Assert.Equal(EnterpriseId, line.EnterpriseId));
        }

        [Fact]
        public async Task ImportAsync_ReimportingTheSameFile_CreatesNoDuplicates()
        {
            var harness = new Harness();
            var file = Ofx(
                ledgerBalance: "10250.10",
                transactions: [Transaction("F1", "20260805", "1500.00"), Transaction("F2", "20260807", "-249.90")]);

            var first = await harness.Service.ImportAsync(EnterpriseId, file);
            var second = await harness.Service.ImportAsync(EnterpriseId, file);

            Assert.Equal(2, first.NewLines);

            Assert.Equal(0, second.NewLines);
            Assert.Equal(2, second.TotalLines);
            Assert.Equal(2, harness.Lines.Count);
        }

        [Fact]
        public async Task ImportAsync_ADuplicatedFitIdInsideOneFile_IsStoredOnce()
        {
            var harness = new Harness();

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260805", "100.00"), Transaction("F1", "20260805", "100.00")]));

            Assert.Equal(1, result.NewLines);
            Assert.Single(harness.Lines);
        }

        [Theory]
        [InlineData("20260805", true)]
        [InlineData("20260804", false)]
        [InlineData("20260811", true)]
        [InlineData("20260812", false)]
        [InlineData("20260808", true)]
        public async Task ImportAsync_MatchesOnlyInsideTheThreeDayWindow(string dueDate, bool shouldMatch)
        {
            var harness = new Harness();
            harness.Payables.Add(new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                Amount = 249.90m,
                DueDate = DateTime.ParseExact(dueDate, "yyyyMMdd", null),
                IsActive = true
            });

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260808", "-249.90")]));

            Assert.Equal(shouldMatch ? 1 : 0, result.MatchedLines);
            Assert.Equal(shouldMatch ? 0 : 1, result.UnmatchedLines);
        }

        [Fact]
        public async Task ImportAsync_MatchesACreditAgainstAnOrderNotAPayable()
        {
            var harness = new Harness();
            var orderId = Guid.NewGuid();

            harness.Orders.Add(new Order
            {
                Id = orderId,
                EnterpriseId = EnterpriseId,
                TotalValue = 1500.00m,
                PaymentScheduledDate = new DateTime(2026, 8, 6),
                IsActive = true
            });

            harness.Payables.Add(new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                Amount = 1500.00m,
                DueDate = new DateTime(2026, 8, 6),
                IsActive = true
            });

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260805", "1500.00")]));

            Assert.Equal(1, result.MatchedLines);
            Assert.Equal(orderId, harness.Lines.Single().MatchedReferenceId);
        }

        [Fact]
        public async Task ImportAsync_TwoEqualCandidates_AreLeftForAHuman()
        {
            var harness = new Harness();

            for (var i = 0; i < 2; i++)
            {
                harness.Payables.Add(new PayableBill
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = EnterpriseId,
                    Amount = 249.90m,
                    DueDate = new DateTime(2026, 8, 8),
                    IsActive = true
                });
            }

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260808", "-249.90")]));

            Assert.Equal(1, result.AmbiguousLines);
            Assert.Equal(0, result.MatchedLines);
            Assert.Null(harness.Lines.Single().MatchedReferenceId);
        }

        [Fact]
        public async Task ImportAsync_MatchesAFeriasBillOnWhatActuallyLeftTheBank()
        {
            var harness = new Harness();
            var billId = Guid.NewGuid();
            harness.Payables.Add(new PayableBill
            {
                Id = billId,
                EnterpriseId = EnterpriseId,
                Amount = 1500.00m,
                GrossAmount = 4000.00m,
                BillType = (int)EnumPayableBillType.Vacation,
                DueDate = new DateTime(2026, 8, 8),
                IsActive = true
            });

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260808", "-4000.00")]));

            Assert.Equal(1, result.MatchedLines);
            Assert.Equal(billId, harness.Lines.Single().MatchedReferenceId);
        }

        [Fact]
        public async Task ImportAsync_OnePayable_IsNotClaimedByTwoStatementLines()
        {
            var harness = new Harness();
            harness.Payables.Add(new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                Amount = 100.00m,
                DueDate = new DateTime(2026, 8, 8),
                IsActive = true
            });

            var result = await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260808", "-100.00"), Transaction("F2", "20260808", "-100.00")]));

            Assert.Equal(1, result.MatchedLines);
            Assert.Equal(1, result.UnmatchedLines);
        }

        [Fact]
        public async Task GetVarianceAsync_ReportsTheGapWithoutTouchingTheAppComputedBalance()
        {
            var harness = new Harness(currentBalance: 10_000.00m);

            await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "9750.25",
                transactions: [Transaction("F1", "20260805", "100.00")]));

            var variance = await harness.Service.GetVarianceAsync(EnterpriseId);

            Assert.Equal(10_000.00m, variance.CurrentBalance);
            Assert.Equal(9750.25m, variance.StatementBalance);
            Assert.Equal(249.75m, variance.Variance);

            Assert.Equal(10_000.00m, harness.Enterprise.CurrentBalance);
        }

        [Fact]
        public async Task GetVarianceAsync_WithNoStatementImported_ReportsNoVariance()
        {
            var harness = new Harness(currentBalance: 10_000.00m);

            var variance = await harness.Service.GetVarianceAsync(EnterpriseId);

            Assert.Equal(10_000.00m, variance.CurrentBalance);
            Assert.Null(variance.StatementBalance);

            Assert.Null(variance.Variance);
        }

        [Fact]
        public async Task GetUnclearedAsync_ReturnsWhatCouldNotBeMatchedAndNotWhatCould()
        {
            var harness = new Harness();
            harness.Payables.Add(new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                Amount = 100.00m,
                DueDate = new DateTime(2026, 8, 8),
                IsActive = true
            });

            await harness.Service.ImportAsync(EnterpriseId, Ofx(
                ledgerBalance: "0.00",
                transactions: [Transaction("F1", "20260808", "-100.00"), Transaction("F2", "20260808", "-77.77")]));

            var uncleared = (await harness.Service.GetUnclearedAsync(EnterpriseId)).ToList();

            Assert.Equal(["F2"], uncleared.Select(line => line.FitId));
            Assert.Equal("Unmatched", uncleared.Single().Status);
        }

        private static string Transaction(string fitId, string posted, string amount) => $"""
            <STMTTRN>
            <TRNTYPE>{(amount.StartsWith('-') ? "DEBIT" : "CREDIT")}
            <DTPOSTED>{posted}120000[-3:BRT]
            <TRNAMT>{amount}
            <FITID>{fitId}
            <MEMO>MOVIMENTO {fitId}
            </STMTTRN>
            """;

        private static byte[] Ofx(string ledgerBalance, string[] transactions) => Encoding.UTF8.GetBytes($"""
            OFXHEADER:100
            <OFX>
            <BANKACCTFROM>
            <BANKID>341
            <ACCTID>12345-6
            </BANKACCTFROM>
            <BANKTRANLIST>
            {string.Join("\n", transactions)}
            </BANKTRANLIST>
            <LEDGERBAL>
            <BALAMT>{ledgerBalance}
            <DTASOF>20260810235959[-3:BRT]
            </LEDGERBAL>
            </OFX>
            """);

        /// <summary>
        /// In-memory stand-ins for the repositories. Writes land in the lists so a second import
        /// genuinely sees what the first one wrote, which is what makes the idempotency test real
        /// rather than a restatement of the mock setup.
        /// </summary>
        #region Resolving a line by hand

        [Fact]
        public async Task ResolveAsync_PointingADebitAtAPayable_ClearsItFromTheDesk()
        {
            var harness = new Harness();
            var payableId = Guid.NewGuid();
            harness.Payables.Add(new PayableBill
            {
                Id = payableId,
                EnterpriseId = EnterpriseId,
                Amount = 480.00m,
                DueDate = new DateTime(2026, 7, 1),
                IsActive = true
            });

            var line = harness.AddLine(amount: -480.00m, postedAt: new DateTime(2026, 8, 8));

            var resolved = await harness.Service.ResolveAsync(EnterpriseId, line.Id, payableId);

            Assert.Equal(nameof(BankStatementLineStatus.Matched), resolved.Status);
            Assert.Equal(payableId, resolved.MatchedReferenceId);
            Assert.Empty(await harness.Service.GetUnclearedAsync(EnterpriseId));
        }

        [Fact]
        public async Task ResolveAsync_WithNoDocument_MarksTheLineIgnored()
        {
            var harness = new Harness();
            var line = harness.AddLine(amount: -32.90m, postedAt: new DateTime(2026, 8, 8));

            var resolved = await harness.Service.ResolveAsync(EnterpriseId, line.Id, matchedReferenceId: null);

            Assert.Equal(nameof(BankStatementLineStatus.Ignored), resolved.Status);
            Assert.Null(resolved.MatchedReferenceId);
            Assert.Empty(await harness.Service.GetUnclearedAsync(EnterpriseId));
            Assert.Equal(0, (await harness.Service.GetVarianceAsync(EnterpriseId)).UnclearedLines);
        }

        [Fact]
        public async Task ResolveAsync_AfterBeingIgnored_CanStillBeMatched()
        {
            var harness = new Harness();
            var payableId = Guid.NewGuid();
            harness.Payables.Add(new PayableBill
            {
                Id = payableId,
                EnterpriseId = EnterpriseId,
                Amount = 100.00m,
                DueDate = new DateTime(2026, 8, 8),
                IsActive = true
            });

            var line = harness.AddLine(amount: -100.00m, postedAt: new DateTime(2026, 8, 8));

            await harness.Service.ResolveAsync(EnterpriseId, line.Id, matchedReferenceId: null);
            var resolved = await harness.Service.ResolveAsync(EnterpriseId, line.Id, payableId);

            Assert.Equal(nameof(BankStatementLineStatus.Matched), resolved.Status);
            Assert.Equal(payableId, resolved.MatchedReferenceId);
        }

        [Fact]
        public async Task ResolveAsync_ACreditPointedAtAPayable_IsRefused()
        {
            var harness = new Harness();
            var payableId = Guid.NewGuid();
            harness.Payables.Add(new PayableBill
            {
                Id = payableId,
                EnterpriseId = EnterpriseId,
                Amount = 100.00m,
                DueDate = new DateTime(2026, 8, 8),
                IsActive = true
            });

            var line = harness.AddLine(amount: 100.00m, postedAt: new DateTime(2026, 8, 8));

            await Assert.ThrowsAsync<NotFoundDatabaseException>(
                () => harness.Service.ResolveAsync(EnterpriseId, line.Id, payableId));
        }

        [Fact]
        public async Task ResolveAsync_ADocumentAlreadyClaimedByAnotherLine_IsRefused()
        {
            var harness = new Harness();
            var payableId = Guid.NewGuid();
            harness.Payables.Add(new PayableBill
            {
                Id = payableId,
                EnterpriseId = EnterpriseId,
                Amount = 100.00m,
                DueDate = new DateTime(2026, 8, 8),
                IsActive = true
            });

            var first = harness.AddLine(amount: -100.00m, postedAt: new DateTime(2026, 8, 8));
            var second = harness.AddLine(amount: -100.00m, postedAt: new DateTime(2026, 8, 9));

            await harness.Service.ResolveAsync(EnterpriseId, first.Id, payableId);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.ResolveAsync(EnterpriseId, second.Id, payableId));
        }

        [Fact]
        public async Task ResolveAsync_AnotherTenantsLine_IsNotFound()
        {
            var harness = new Harness();
            var line = harness.AddLine(amount: -100.00m, postedAt: new DateTime(2026, 8, 8));
            line.EnterpriseId = Guid.NewGuid();

            await Assert.ThrowsAsync<NotFoundDatabaseException>(
                () => harness.Service.ResolveAsync(EnterpriseId, line.Id, matchedReferenceId: null));
        }

        #endregion

        private sealed class Harness
        {
            public List<BankStatementLine> Lines { get; } = [];
            public List<BankStatementImport> Imports { get; } = [];
            public List<Order> Orders { get; } = [];
            public List<PayableBill> Payables { get; } = [];
            public Enterprise Enterprise { get; }
            public BankReconciliationService Service { get; }

            /// <summary>A statement line already sitting on the desk, as an import would have left it.</summary>
            public BankStatementLine AddLine(decimal amount, DateTime postedAt)
            {
                var line = new BankStatementLine
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = EnterpriseId,
                    ImportId = Guid.NewGuid(),
                    BankId = "001",
                    AccountId = "12345",
                    FitId = Guid.NewGuid().ToString("N"),
                    PostedAt = postedAt,
                    Amount = amount,
                    TrnType = amount > 0 ? "CREDIT" : "DEBIT",
                    Memo = "M",
                    Status = (int)BankStatementLineStatus.Unmatched,
                    IsActive = true
                };

                Lines.Add(line);
                return line;
            }

            public Harness(decimal currentBalance = 0m)
            {
                Enterprise = new Enterprise { Id = EnterpriseId, Name = "E", IsActive = true, CurrentBalance = currentBalance };

                var lineRepository = new Mock<IRepository<BankStatementLine>>();
                lineRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<BankStatementLine, bool>>>()))
                    .ReturnsAsync((Expression<Func<BankStatementLine, bool>>? predicate) =>
                        Lines.Where(predicate!.Compile()).ToList());
                lineRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<BankStatementLine, bool>>>()))
                    .ReturnsAsync((int? _, int? _, Expression<Func<BankStatementLine, bool>>? predicate) =>
                        Lines.Where(predicate!.Compile()).ToList());
                lineRepository
                    .Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                    .ReturnsAsync((Guid id) => Lines.FirstOrDefault(line => line.Id == id)!);
                lineRepository
                    .Setup(x => x.Update(It.IsAny<BankStatementLine>()))
                    .Returns((BankStatementLine line) => line);
                lineRepository
                    .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                lineRepository
                    .Setup(x => x.ExecuteInTransactionAsync(
                        It.IsAny<Func<Task<BankStatementLineDTO>>>(), It.IsAny<CancellationToken>()))
                    .Returns((Func<Task<BankStatementLineDTO>> operation, CancellationToken _) => operation());
                lineRepository
                    .Setup(x => x.CreateRangeAsync(It.IsAny<IEnumerable<BankStatementLine>>()))
                    .ReturnsAsync((IEnumerable<BankStatementLine> created) =>
                    {
                        Lines.AddRange(created);
                        return created;
                    });

                var importRepository = new Mock<IRepository<BankStatementImport>>();
                importRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<BankStatementImport, bool>>>()))
                    .ReturnsAsync((Expression<Func<BankStatementImport, bool>>? predicate) =>
                        Imports.Where(predicate!.Compile()).ToList());
                importRepository
                    .Setup(x => x.CreateAsync(It.IsAny<BankStatementImport>()))
                    .ReturnsAsync((BankStatementImport created) =>
                    {
                        Imports.Add(created);
                        return created;
                    });

                var orderRepository = new Mock<IRepository<Order>>();
                orderRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                    .ReturnsAsync((Expression<Func<Order, bool>>? predicate) =>
                        Orders.Where(predicate!.Compile()).ToList());
                orderRepository
                    .Setup(x => x.GetPagedAsync(
                        It.IsAny<Expression<Func<Order, bool>>?>(),
                        It.IsAny<Func<IQueryable<Order>, IOrderedQueryable<Order>>?>(),
                        It.IsAny<int?>(),
                        It.IsAny<int?>()))
                    .ReturnsAsync((
                        Expression<Func<Order, bool>>? predicate,
                        Func<IQueryable<Order>, IOrderedQueryable<Order>>? _,
                        int? pageNumber,
                        int? pageSize) => Page(Orders, predicate, pageNumber, pageSize));

                var payableRepository = new Mock<IRepository<PayableBill>>();
                payableRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                    .ReturnsAsync((Expression<Func<PayableBill, bool>>? predicate) =>
                        Payables.Where(predicate!.Compile()).ToList());
                payableRepository
                    .Setup(x => x.GetPagedAsync(
                        It.IsAny<Expression<Func<PayableBill, bool>>?>(),
                        It.IsAny<Func<IQueryable<PayableBill>, IOrderedQueryable<PayableBill>>?>(),
                        It.IsAny<int?>(),
                        It.IsAny<int?>()))
                    .ReturnsAsync((
                        Expression<Func<PayableBill, bool>>? predicate,
                        Func<IQueryable<PayableBill>, IOrderedQueryable<PayableBill>>? _,
                        int? pageNumber,
                        int? pageSize) => Page(Payables, predicate, pageNumber, pageSize));

                var enterpriseRepository = new Mock<IRepository<Enterprise>>();
                enterpriseRepository.Setup(x => x.GetByIdAsync(EnterpriseId)).ReturnsAsync(Enterprise);

                Service = new BankReconciliationService(
                    importRepository.Object,
                    lineRepository.Object,
                    orderRepository.Object,
                    payableRepository.Object,
                    enterpriseRepository.Object);
            }

            /// <summary>
            /// What the real GetPagedAsync does to a list: filter, then take the page when one is
            /// asked for. No Includes and no implicit IsActive filter, which is the point of the
            /// service using it.
            /// </summary>
            private static (IReadOnlyList<T> Items, int TotalItems) Page<T>(
                IEnumerable<T> source,
                Expression<Func<T, bool>>? predicate,
                int? pageNumber,
                int? pageSize)
            {
                var filtered = (predicate == null ? source : source.Where(predicate.Compile())).ToList();
                if (pageNumber == null || pageSize == null)
                    return (filtered, filtered.Count);

                return (filtered.Skip((pageNumber.Value - 1) * pageSize.Value).Take(pageSize.Value).ToList(), filtered.Count);
            }
        }
    }
}
