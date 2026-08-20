using System.Linq.Expressions;
using System.Text;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
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

            // The whole point of keying on FITID. A second upload of the same statement — which is
            // exactly what an operator does when they are unsure the first one worked — must not
            // double the movement behind the variance figure.
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
        // The statement posts on the 8th. A payable dated anywhere inside ±3 days is the same event
        // seen from two sides; the 4th and the 12th are four days out and must not be claimed.
        [InlineData("20260805", true)]   // 3 days early — the boundary itself
        [InlineData("20260804", false)]  // 4 days early — just outside
        [InlineData("20260811", true)]   // 3 days late — the boundary itself
        [InlineData("20260812", false)]  // 4 days late — just outside
        [InlineData("20260808", true)]   // same day
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

            // Same amount on the payables side. The sign is what keeps the credit off it.
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

            // Not guessed at. Picking either one by date proximity would reconcile the wrong invoice
            // and, worse, stop looking like work that still needs doing.
            Assert.Equal(1, result.AmbiguousLines);
            Assert.Equal(0, result.MatchedLines);
            Assert.Null(harness.Lines.Single().MatchedReferenceId);
        }

        [Fact]
        public async Task ImportAsync_MatchesAFeriasBillOnWhatActuallyLeftTheBank()
        {
            // A férias bill's Amount is netted by the provision payroll already accrued for it
            // (SPEC-hr-module §4 P5a), but the employee is paid the gross, so that is the figure
            // the statement carries.
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

            // Two identical debits, one invoice: the first consumes it, the second is left uncleared.
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

            // The balance is app-computed and stays that way. An import that silently rewrote it to
            // the bank's figure would make the variance permanently zero and the screen useless.
            Assert.Equal(10_000.00m, harness.Enterprise.CurrentBalance);
        }

        [Fact]
        public async Task GetVarianceAsync_WithNoStatementImported_ReportsNoVariance()
        {
            var harness = new Harness(currentBalance: 10_000.00m);

            var variance = await harness.Service.GetVarianceAsync(EnterpriseId);

            Assert.Equal(10_000.00m, variance.CurrentBalance);
            Assert.Null(variance.StatementBalance);

            // Null, not 10 000: a "variance" of the entire balance would read as a catastrophic
            // break rather than as nothing having been imported yet.
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
        private sealed class Harness
        {
            public List<BankStatementLine> Lines { get; } = [];
            public List<BankStatementImport> Imports { get; } = [];
            public List<Order> Orders { get; } = [];
            public List<PayableBill> Payables { get; } = [];
            public Enterprise Enterprise { get; }
            public BankReconciliationService Service { get; }

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

                var payableRepository = new Mock<IRepository<PayableBill>>();
                payableRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                    .ReturnsAsync((Expression<Func<PayableBill, bool>>? predicate) =>
                        Payables.Where(predicate!.Compile()).ToList());

                var enterpriseRepository = new Mock<IRepository<Enterprise>>();
                enterpriseRepository.Setup(x => x.GetByIdAsync(EnterpriseId)).ReturnsAsync(Enterprise);

                Service = new BankReconciliationService(
                    importRepository.Object,
                    lineRepository.Object,
                    orderRepository.Object,
                    payableRepository.Object,
                    enterpriseRepository.Object);
            }
        }
    }
}
