using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;
using BillEntity = EvangelionERPV2.Shared.Entities.Bill;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// The settle / reissue / allocate paths. Together they are what turns the boleto from a
    /// correct-looking document into a receivable the ERP can actually reason about.
    /// </summary>
    public class BillPaymentStateTests
    {
        #region Settle

        [Fact]
        public async Task SettleAsync_WithValidData_RecordsTheSettlement()
        {
            var harness = new Harness();
            var bill = harness.GivenBill(BillStatus.Registered);
            var settledAt = DateTime.UtcNow.AddDays(-1);

            var result = await harness.Service.SettleAsync(bill.Id, harness.EnterpriseId, 112.50m, settledAt, " AUTH-9 ");

            Assert.NotNull(result);
            Assert.Equal(BillStatus.Paid, result!.Status);
            Assert.Equal(settledAt, result.PaidAt);
            Assert.Equal(112.50m, result.PaidAmount);
            Assert.Equal("AUTH-9", result.SettlementProtocol);
            harness.BillRepo.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SettleAsync_WhenAlreadySettled_Throws()
        {
            var harness = new Harness();
            var bill = harness.GivenBill(BillStatus.Paid);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.SettleAsync(bill.Id, harness.EnterpriseId, 100m, null, null));
        }

        [Fact]
        public async Task SettleAsync_WhenCancelled_Throws()
        {
            var harness = new Harness();
            var bill = harness.GivenBill(BillStatus.Cancelled);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.SettleAsync(bill.Id, harness.EnterpriseId, 100m, null, null));
        }

        [Fact]
        public async Task SettleAsync_WithAFutureDate_Throws()
        {
            var harness = new Harness();
            var bill = harness.GivenBill(BillStatus.Registered);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.SettleAsync(bill.Id, harness.EnterpriseId, 100m, DateTime.UtcNow.AddDays(2), null));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task SettleAsync_WithANonPositiveAmount_Throws(decimal paidAmount)
        {
            var harness = new Harness();
            harness.GivenBill(BillStatus.Registered);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.SettleAsync(Guid.NewGuid(), harness.EnterpriseId, paidAmount, null, null));
        }

        [Fact]
        public async Task SettleAsync_FromAnotherEnterprise_ReturnsNullWithoutWriting()
        {
            var harness = new Harness();
            var bill = harness.GivenBill(BillStatus.Registered);

            var result = await harness.Service.SettleAsync(bill.Id, Guid.NewGuid(), 100m, null, null);

            Assert.Null(result);
            harness.BillRepo.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        #endregion

        #region Reissue

        [Fact]
        public async Task ReissueAsync_CancelsTheCurrentBillBeforeIssuingTheReplacement()
        {
            var harness = new Harness();
            var current = harness.GivenBill(BillStatus.Registered);

            harness.BillRepoCustom
                .SetupSequence(r => r.GetByOrderIdAsync(harness.Order.Id))
                .ReturnsAsync(current)
                .ReturnsAsync((BillEntity?)null);

            var reissued = await harness.Service.ReissueAsync(harness.Order.Id, harness.EnterpriseId);

            Assert.Equal(BillStatus.Cancelled, current.Status);
            Assert.NotNull(reissued);
            Assert.NotEqual(current.Id, reissued!.Id);
            Assert.Equal(BillStatus.Registered, reissued.Status);
        }

        [Fact]
        public async Task ReissueAsync_WhenTheCurrentBillIsSettled_Throws()
        {
            var harness = new Harness();
            var current = harness.GivenBill(BillStatus.Paid);
            harness.BillRepoCustom.Setup(r => r.GetByOrderIdAsync(harness.Order.Id)).ReturnsAsync(current);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.ReissueAsync(harness.Order.Id, harness.EnterpriseId));
        }

        [Fact]
        public async Task ReissueAsync_FromAnotherEnterprise_ReturnsNull()
        {
            var harness = new Harness();
            harness.GivenBill(BillStatus.Registered);

            Assert.Null(await harness.Service.ReissueAsync(harness.Order.Id, Guid.NewGuid()));
        }

        #endregion

        #region NossoNumero allocation

        [Fact]
        public async Task GenerateAsync_UsesTheAllocatedNumberPaddedToTheContractedWidth()
        {
            var harness = new Harness();
            harness.BillRepoCustom.Setup(r => r.GetByOrderIdAsync(harness.Order.Id)).ReturnsAsync((BillEntity?)null);
            harness.GivenAllocation(42);

            var bill = await harness.Service.GenerateAsync(harness.Order.Id, harness.EnterpriseId);

            Assert.NotNull(bill);
            Assert.All(bill!.OurNumber, character => Assert.True(char.IsDigit(character)));
            Assert.Contains("00000042", bill.OurNumber, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_WhenNoCounterExistsYet_SeedsItFromSettingsAndRetries()
        {
            var harness = new Harness();
            harness.Settings.NossoNumeroStart = 5000;
            harness.Settings.NossoNumeroCeiling = 9999;
            harness.BillRepoCustom.Setup(r => r.GetByOrderIdAsync(harness.Order.Id)).ReturnsAsync((BillEntity?)null);

            harness.BillRepoCustom
                .SetupSequence(r => r.AllocateNextNumberAsync(
                    harness.EnterpriseId, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long?)null)
                .ReturnsAsync(5000L);

            BillNumberSequence? seeded = null;
            harness.SequenceRepo
                .Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<BillNumberSequence, bool>>>()))
                .ReturnsAsync([]);
            harness.SequenceRepo
                .Setup(r => r.CreateAsync(It.IsAny<BillNumberSequence>()))
                .ReturnsAsync((BillNumberSequence sequence) => seeded = sequence);

            var bill = await harness.Service.GenerateAsync(harness.Order.Id, harness.EnterpriseId);

            Assert.NotNull(seeded);
            Assert.Equal(5000, seeded!.NextNumber);
            Assert.Equal(9999, seeded.Ceiling);
            Assert.EndsWith("5000", bill!.OurNumber, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_WhenTheRangeIsExhausted_ThrowsInsteadOfIssuingOutOfRange()
        {
            var harness = new Harness();
            harness.BillRepoCustom.Setup(r => r.GetByOrderIdAsync(harness.Order.Id)).ReturnsAsync((BillEntity?)null);
            harness.BillRepoCustom
                .Setup(r => r.AllocateNextNumberAsync(
                    It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long?)null);
            harness.SequenceRepo
                .Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<BillNumberSequence, bool>>>()))
                .ReturnsAsync([new BillNumberSequence { Id = Guid.NewGuid(), NextNumber = 100, Ceiling = 99 }]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => harness.Service.GenerateAsync(harness.Order.Id, harness.EnterpriseId));

            harness.SequenceRepo.Verify(r => r.CreateAsync(It.IsAny<BillNumberSequence>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenABillAlreadyExists_DoesNotBurnANumber()
        {
            var harness = new Harness();
            harness.GivenBill(BillStatus.Registered);

            await harness.Service.GenerateAsync(harness.Order.Id, harness.EnterpriseId);

            harness.BillRepoCustom.Verify(
                r => r.AllocateNextNumberAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        #endregion

        #region Overdue derivation

        [Theory]
        [InlineData(BillStatus.Registered, -1, BillStatus.Overdue)]
        [InlineData(BillStatus.Registered, 1, BillStatus.Registered)]
        [InlineData(BillStatus.Paid, -1, BillStatus.Paid)]
        [InlineData(BillStatus.Cancelled, -1, BillStatus.Cancelled)]
        public void StatusAsOf_DerivesOverdueWithoutEverStoringIt(
            BillStatus stored, int dueInDays, BillStatus expected)
        {
            var now = DateTime.UtcNow;
            var bill = new BillEntity { Status = stored, DueDate = now.AddDays(dueInDays) };

            Assert.Equal(expected, bill.StatusAsOf(now));
            Assert.Equal(stored, bill.Status);
        }

        #endregion

        #region History

        [Fact]
        public async Task GetHistoryAsync_ReturnsSupersededBillsToo()
        {
            var harness = new Harness();
            var history = new List<BillEntity>
            {
                new() { Id = Guid.NewGuid(), OrderId = harness.Order.Id, Status = BillStatus.Registered },
                new() { Id = Guid.NewGuid(), OrderId = harness.Order.Id, Status = BillStatus.Cancelled }
            };
            harness.BillRepoCustom.Setup(r => r.GetAllByOrderIdAsync(harness.Order.Id)).ReturnsAsync(history);

            var result = await harness.Service.GetHistoryAsync(harness.Order.Id, harness.EnterpriseId);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetHistoryAsync_FromAnotherEnterprise_ReturnsEmpty()
        {
            var harness = new Harness();

            Assert.Empty(await harness.Service.GetHistoryAsync(harness.Order.Id, Guid.NewGuid()));
        }

        #endregion

        /// <summary>
        /// One order in one enterprise, wired to mocks that let a real BoletoNetCore document be
        /// built. Settings mirror BillsServiceTests so the two suites agree on what "valid" means.
        /// </summary>
        private sealed class Harness
        {
            public Harness()
            {
                Order = new Order(
                    DateTime.UtcNow.AddDays(5),
                    DateTime.UtcNow.AddDays(5),
                    100,
                    EnterpriseId,
                    null,
                    new List<OrderedProduct>(),
                    Guid.NewGuid())
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow
                };

                OrderRepo.Setup(r => r.GetByIdAsync(Order.Id)).ReturnsAsync(Order);
                BillRepo.Setup(r => r.CreateAsync(It.IsAny<BillEntity>())).ReturnsAsync((BillEntity bill) => bill);
                BillRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                GivenAllocation(1);

                Service = new BillsService(
                    BillRepo.Object,
                    BillRepoCustom.Object,
                    OrderRepo.Object,
                    new Mock<IRepository<Customer>>().Object,
                    new Mock<IRepository<Enterprise>>().Object,
                    SequenceRepo.Object,
                    () => new Mock<IEmailService<EmailStructure>>().Object,
                    Options.Create(Settings));
            }

            public Guid EnterpriseId { get; } = Guid.NewGuid();
            public Order Order { get; }
            public BillsSettings Settings { get; } = CreateValidSettings();
            public Mock<IRepository<BillEntity>> BillRepo { get; } = new();
            public Mock<IBillsRepository<BillEntity>> BillRepoCustom { get; } = new();
            public Mock<IRepository<Order>> OrderRepo { get; } = new();
            public Mock<IRepository<BillNumberSequence>> SequenceRepo { get; } = new();
            public BillsService Service { get; }

            public void GivenAllocation(long number) =>
                BillRepoCustom
                    .Setup(r => r.AllocateNextNumberAsync(
                        It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(number);

            public BillEntity GivenBill(BillStatus status)
            {
                var bill = new BillEntity
                {
                    Id = Guid.NewGuid(),
                    OrderId = Order.Id,
                    Amount = 100,
                    DueDate = DateTime.UtcNow.AddDays(5),
                    Status = status,
                    IsActive = true
                };

                BillRepo.Setup(r => r.GetByIdAsync(bill.Id)).ReturnsAsync(bill);
                BillRepoCustom.Setup(r => r.GetByOrderIdAsync(Order.Id)).ReturnsAsync(bill);

                return bill;
            }

            private static BillsSettings CreateValidSettings() => new()
            {
                Enabled = true,
                AttachPdfToBillEmail = false,
                BankCode = 33,
                BeneficiaryDocument = "12345678000195",
                BeneficiaryName = "Beneficiario Teste",
                BeneficiaryAddress = "Rua Beneficiario",
                BeneficiaryAddressNumber = "123",
                BeneficiaryNeighborhood = "Centro",
                BeneficiaryCity = "Cidade",
                BeneficiaryState = "SP",
                BeneficiaryZipCode = "12345678",
                BeneficiaryCode = "1234567",
                TransmissionCode = "123400001234567",
                Agency = "1234",
                AgencyDigit = "5",
                Account = "12345678",
                AccountDigit = "9",
                Wallet = "101",
                WalletType = 1,
                RegistrationType = 1,
                PrintType = 2,
                DocumentType = 1,
                NossoNumeroLength = 8,
                DefaultPayerDocument = "11144477735",
                DefaultPayerName = "Pagador Teste",
                DefaultPayerAddress = "Rua Pagador"
            };
        }
    }
}
