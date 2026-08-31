using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// Raising a PIX charge, reading it, and refusing to raise one we should not.
    /// </summary>
    /// <remarks>
    /// Every gate here is asserted before Stripe is reached — verified by a strict provider mock
    /// that fails the test if it is called at all. A shop discovers it cannot take PIX from a
    /// greyed-out button, never from a customer standing at the counter with a bank app open.
    /// </remarks>
    public class PixChargeTests : IDisposable
    {
        private readonly PdvTestContext _database = new();
        private readonly Guid _enterpriseId = Guid.NewGuid();

        public void Dispose() => _database.Dispose();

        [Fact]
        public async Task StartingACharge_PersistsItPendingWithItsQr()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var provider = ProviderReturning("pi_1", "00020126...br.gov.bcb.pix");
            var service = CreateService(context, provider);

            var charge = await service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId);

            Assert.Equal((int)EnumPdvChargeStatus.Pending, charge.Status);
            Assert.Equal("00020126...br.gov.bcb.pix", charge.QrPayload);
            Assert.Equal(60m, charge.Amount);
            Assert.Equal(1, await context.PdvCharge.CountAsync());
        }

        [Fact]
        public async Task StartingTwiceForOneClientSaleId_ReturnsTheSameCharge()
        {
            // The failure this prevents is a customer who pays twice and an operator who cannot
            // tell. A resend after a lost response must find the intent, never open a second one.
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var provider = ProviderReturning("pi_1", "qr");
            var service = CreateService(context, provider);
            var clientSaleId = Guid.NewGuid();
            var request = new StartPixChargeRequestDTO { ClientSaleId = clientSaleId, Amount = 60m };

            var first = await service.StartPixChargeAsync(request, _enterpriseId);
            var second = await service.StartPixChargeAsync(request, _enterpriseId);

            Assert.Equal(first.ChargeId, second.ChargeId);
            Assert.Equal(1, await context.PdvCharge.CountAsync());
            provider.Verify(p => p.CreateAsync(It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AShopWithNoConnectedAccount_IsRefusedBeforeStripeIsCalled()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context, accountId: null);
            var service = CreateService(context, StrictProvider());

            await Assert.ThrowsAsync<ArgumentException>(() => service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId));
        }

        [Fact]
        public async Task AShopWithoutThePixInvite_IsRefusedBeforeStripeIsCalled()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context, pixEnabled: false);
            var service = CreateService(context, StrictProvider());

            await Assert.ThrowsAsync<ArgumentException>(() => service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId));
        }

        [Fact]
        public async Task AnAmountOverThreeThousand_IsRefused()
        {
            // R$ 3.000 is Stripe's PIX ceiling. Splitting it across several charges is deliberately
            // not automated: multiple pending intents against one sale is a partial-payment mess.
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var service = CreateService(context, StrictProvider());

            await Assert.ThrowsAsync<ArgumentException>(() => service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 3000.01m }, _enterpriseId));
        }

        [Fact]
        public async Task AnAmountUnderFiftyCentavos_IsRefused()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var service = CreateService(context, StrictProvider());

            await Assert.ThrowsAsync<ArgumentException>(() => service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 0.49m }, _enterpriseId));
        }

        [Fact]
        public async Task GetChargeStatus_ReadsOurOwnRowAndNeverCallsStripe()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var service = CreateService(context, ProviderReturning("pi_1", "qr"));
            var started = await service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId);

            // A strict provider on the read path: any call at all fails the test.
            var reader = CreateService(context, StrictProvider());
            var read = await reader.GetChargeAsync(started.ChargeId, _enterpriseId);

            Assert.NotNull(read);
            Assert.Equal(started.ChargeId, read!.ChargeId);
        }

        [Fact]
        public async Task GetChargeStatus_NeverReturnsAnotherTenantsCharge()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var service = CreateService(context, ProviderReturning("pi_1", "qr"));
            var started = await service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId);

            Assert.Null(await service.GetChargeAsync(started.ChargeId, Guid.NewGuid()));
        }

        [Fact]
        public async Task CancellingAPendingCharge_MovesItToCancelled()
        {
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var provider = ProviderReturning("pi_1", "qr");
            var service = CreateService(context, provider);
            var started = await service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId);

            var cancelled = await service.CancelPixChargeAsync(started.ChargeId, _enterpriseId);

            Assert.Equal((int)EnumPdvChargeStatus.Cancelled, cancelled.Status);
            provider.Verify(p => p.CancelAsync("pi_1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CancellingAnAlreadyPaidCharge_LeavesItPaid()
        {
            // A customer who paid a second before Esc must not have their payment marked cancelled.
            using var context = _database.CreateContext();
            await GiveShopAPixAccountAsync(context);
            var provider = ProviderReturning("pi_1", "qr");
            var service = CreateService(context, provider);
            var started = await service.StartPixChargeAsync(
                new StartPixChargeRequestDTO { ClientSaleId = Guid.NewGuid(), Amount = 60m }, _enterpriseId);

            var paid = await context.PdvCharge.FirstAsync(x => x.Id == started.ChargeId);
            paid.Status = (int)EnumPdvChargeStatus.Paid;
            await context.SaveChangesAsync();

            var result = await service.CancelPixChargeAsync(started.ChargeId, _enterpriseId);

            Assert.Equal((int)EnumPdvChargeStatus.Paid, result.Status);
            provider.Verify(p => p.CancelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private async Task GiveShopAPixAccountAsync(AppDbContext context,
            string? accountId = "acct_123", bool pixEnabled = true)
        {
            context.Enterprise.Add(new Enterprise
            {
                Id = _enterpriseId,
                Name = "Mercado Evangelion",
                StripeConnectedAccountId = accountId,
                PixPaymentsEnabled = pixEnabled
            });

            await context.SaveChangesAsync();
        }

        /// <summary>A provider that fails the test if it is touched at all.</summary>
        private static Mock<StripePixProvider> StrictProvider() => NewProvider(MockBehavior.Strict);

        private static Mock<StripePixProvider> ProviderReturning(string intentId, string qr)
        {
            var mock = NewProvider(MockBehavior.Loose);
            mock.Setup(p => p.CreateAsync(It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixIntent(intentId, qr, DateTime.UtcNow.AddMinutes(30), "requires_action"));
            mock.Setup(p => p.CancelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return mock;
        }

        private static Mock<StripePixProvider> NewProvider(MockBehavior behavior) =>
            new(behavior,
                new AWSKMSKeyProvider(new Mock<Amazon.SecretsManager.IAmazonSecretsManager>().Object,
                    new ConfigurationBuilder().AddInMemoryCollection().Build()),
                new ConfigurationBuilder().AddInMemoryCollection().Build());

        private PdvService CreateService(AppDbContext context, Mock<StripePixProvider> provider) =>
            new(context,
                new Repository<CashSession>(context),
                new Repository<CashMovement>(context),
                new Repository<PdvSale>(context),
                new Repository<PdvSalePayment>(context),
                new Mock<IOrderService<Order>>().Object,
                new Mock<IProductService<Product>>().Object,
                new Repository<PdvCharge>(context),
                provider.Object);
    }
}
