using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using Moq;
using Xunit;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// What a provider event does to a charge — and what a replayed or late one does not.
    /// </summary>
    /// <remarks>
    /// The signature check is the controller's half and is tested where the controller lives
    /// (EvangelionERPV2.Test); this file is the half that decides what an event is allowed to
    /// change once it has been believed.
    ///
    /// Every provider retries, so an endpoint that is not idempotent is one that will eventually be
    /// delivered twice and record the money twice.
    /// </remarks>
    public class StripeWebhookTests : IDisposable
    {
        private readonly PdvTestContext _database = new();
        private readonly Guid _enterpriseId = Guid.NewGuid();

        public void Dispose() => _database.Dispose();

        [Fact]
        public async Task ASucceededEventMarksTheChargePaidWithItsEndToEndIdAndFee()
        {
            using var context = _database.CreateContext();
            var service = CreateService(context);
            await GiveChargeAsync(context, "pi_1");
            var paidAt = DateTime.UtcNow;

            var changed = await service.ApplyChargeEventAsync(
                "evt_1", "pi_1", (int)EnumPdvChargeStatus.Paid, "E12345678", 0.71m, paidAt);

            var charge = await context.PdvCharge.SingleAsync();
            Assert.True(changed);
            Assert.Equal((int)EnumPdvChargeStatus.Paid, charge.Status);
            Assert.Equal("E12345678", charge.EndToEndId);
            Assert.Equal(0.71m, charge.FeeAmount);
            Assert.Equal("evt_1", charge.ProviderEventId);
        }

        [Fact]
        public async Task ReplayingTheSameEventIdChangesNothing()
        {
            // A webhook endpoint that is not idempotent is a webhook endpoint that will eventually
            // be delivered twice, because every provider retries.
            using var context = _database.CreateContext();
            var service = CreateService(context);
            await GiveChargeAsync(context, "pi_1");

            await service.ApplyChargeEventAsync("evt_1", "pi_1", (int)EnumPdvChargeStatus.Paid, "E1", 0.71m, DateTime.UtcNow);
            var second = await service.ApplyChargeEventAsync("evt_1", "pi_1", (int)EnumPdvChargeStatus.Paid, "E1", 0.71m, DateTime.UtcNow);

            Assert.False(second);
        }

        [Fact]
        public async Task AnEventForAnIntentWeDoNotKnowIsAcceptedAndIgnored()
        {
            // False, not an exception: telling Stripe to retry forever for a charge that is not
            // ours is a queue we never drain.
            using var context = _database.CreateContext();
            var service = CreateService(context);

            Assert.False(await service.ApplyChargeEventAsync(
                "evt_1", "pi_unknown", (int)EnumPdvChargeStatus.Paid, null, null, DateTime.UtcNow));
        }

        [Fact]
        public async Task ALateEventCannotUnsettleAChargeThatAlreadyPaid()
        {
            // Expiry racing settlement is a real delivery order, and the money won.
            using var context = _database.CreateContext();
            var service = CreateService(context);
            await GiveChargeAsync(context, "pi_1");

            await service.ApplyChargeEventAsync("evt_1", "pi_1", (int)EnumPdvChargeStatus.Paid, "E1", 0.71m, DateTime.UtcNow);
            var late = await service.ApplyChargeEventAsync("evt_2", "pi_1", (int)EnumPdvChargeStatus.Expired, null, null, null);

            Assert.False(late);
            Assert.Equal((int)EnumPdvChargeStatus.Paid, (await context.PdvCharge.SingleAsync()).Status);
        }

        [Fact]
        public async Task AChargeIsOnlyEverMovedOutOfPendingOnce()
        {
            using var context = _database.CreateContext();
            var service = CreateService(context);
            await GiveChargeAsync(context, "pi_1");

            await service.ApplyChargeEventAsync("evt_1", "pi_1", (int)EnumPdvChargeStatus.Cancelled, null, null, null);
            var later = await service.ApplyChargeEventAsync("evt_2", "pi_1", (int)EnumPdvChargeStatus.Paid, "E1", 0.71m, DateTime.UtcNow);

            Assert.False(later);
            Assert.Equal((int)EnumPdvChargeStatus.Cancelled, (await context.PdvCharge.SingleAsync()).Status);
        }

        [Fact]
        public async Task AnEventWithAGoodSignatureIsBelievedAndMarksTheChargePaid()
        {
            using var context = _database.CreateContext();
            await GiveChargeAsync(context, "pi_1");
            var handler = CreateHandler(context);
            var json = SucceededEventJson("evt_1", "pi_1");

            Assert.True(await handler.TryHandleAsync(json, SignatureFor(json)));
            Assert.Equal((int)EnumPdvChargeStatus.Paid, (await context.PdvCharge.SingleAsync()).Status);
        }

        [Fact]
        public async Task AnEventWithABadSignatureIsRefusedAndChangesNothing()
        {
            // The only thing standing between a stranger and marking a charge paid.
            using var context = _database.CreateContext();
            await GiveChargeAsync(context, "pi_1");
            var handler = CreateHandler(context);
            var json = SucceededEventJson("evt_1", "pi_1");

            var signedWithTheWrongSecret = SignatureFor(json, secret: "whsec_not_ours");

            Assert.False(await handler.TryHandleAsync(json, signedWithTheWrongSecret));
            Assert.Equal((int)EnumPdvChargeStatus.Pending, (await context.PdvCharge.SingleAsync()).Status);
        }

        [Fact]
        public async Task ABodyAlteredAfterSigningIsRefused()
        {
            // The signature covers the body. Re-pointing the event at a different intent after
            // signing must not survive verification.
            using var context = _database.CreateContext();
            await GiveChargeAsync(context, "pi_1");
            var handler = CreateHandler(context);
            var signature = SignatureFor(SucceededEventJson("evt_1", "pi_other"));

            Assert.False(await handler.TryHandleAsync(SucceededEventJson("evt_1", "pi_1"), signature));
            Assert.Equal((int)EnumPdvChargeStatus.Pending, (await context.PdvCharge.SingleAsync()).Status);
        }

        [Fact]
        public async Task AnEventWithNoSignatureHeaderIsRefused()
        {
            using var context = _database.CreateContext();
            var handler = CreateHandler(context);

            Assert.False(await handler.TryHandleAsync(SucceededEventJson("evt_1", "pi_1"), signatureHeader: null));
            Assert.False(await handler.TryHandleAsync(SucceededEventJson("evt_1", "pi_1"), signatureHeader: "   "));
        }

        [Fact]
        public async Task AStaleTimestampIsRefusedEvenWithARealSignature()
        {
            // Replay protection. The signature is genuine; the clock is not.
            using var context = _database.CreateContext();
            await GiveChargeAsync(context, "pi_1");
            var handler = CreateHandler(context);
            var json = SucceededEventJson("evt_1", "pi_1");
            var longAgo = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();

            Assert.False(await handler.TryHandleAsync(json, SignatureFor(json, timestamp: longAgo)));
            Assert.Equal((int)EnumPdvChargeStatus.Pending, (await context.PdvCharge.SingleAsync()).Status);
        }

        [Fact]
        public async Task AnEventTypeWeDoNotHandleIsAcceptedAndIgnored()
        {
            // 200, not 400: another integration on the same account must not build a retry queue.
            using var context = _database.CreateContext();
            await GiveChargeAsync(context, "pi_1");
            var handler = CreateHandler(context);
            var json = SucceededEventJson("evt_1", "pi_1").Replace(
                "payment_intent.succeeded", "customer.created");

            Assert.True(await handler.TryHandleAsync(json, SignatureFor(json)));
            Assert.Equal((int)EnumPdvChargeStatus.Pending, (await context.PdvCharge.SingleAsync()).Status);
        }

        private const string WebhookSecret = "whsec_test_secret";

        /// <summary>The header exactly as Stripe builds it: <c>t=&lt;unix&gt;,v1=&lt;HMAC-SHA256&gt;</c>.</summary>
        /// <remarks>
        /// Built here rather than stubbed, so these tests exercise the real verification path. A
        /// stubbed verifier would pass whatever it was told and prove nothing.
        /// </remarks>
        private static string SignatureFor(string json, string secret = WebhookSecret, long? timestamp = null)
        {
            var stamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{stamp}.{json}"));

            return $"t={stamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
        }

        /// <summary>
        /// A payment_intent.succeeded envelope, as Stripe sends one.
        /// </summary>
        /// <remarks>
        /// The envelope fields are not decoration: Stripe.net's own EventConverter reads
        /// <c>api_version</c> to pick a deserialiser and throws without it, so a payload trimmed to
        /// just id/type/data never reaches the code under test.
        /// </remarks>
        private static string SucceededEventJson(string eventId, string intentId) =>
            $$"""
            {
              "id": "{{eventId}}",
              "object": "event",
              "api_version": "2024-06-20",
              "created": 1718000000,
              "livemode": false,
              "pending_webhooks": 1,
              "type": "payment_intent.succeeded",
              "request": { "id": null, "idempotency_key": null },
              "data": { "object": { "id": "{{intentId}}", "object": "payment_intent", "amount": 6000, "currency": "brl", "status": "succeeded" } }
            }
            """;

        private StripeWebhookHandler CreateHandler(AppDbContext context)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Stripe:WebhookSecretName"] = "pdv/stripe/webhook"
                })
                .Build();

            return new HandlerWithKnownSecret(CreateService(context),
                new AWSKMSKeyProvider(
                    new Mock<Amazon.SecretsManager.IAmazonSecretsManager>().Object, configuration),
                configuration);
        }

        /// <summary>The real handler, told the secret these tests sign with.</summary>
        private sealed class HandlerWithKnownSecret(
            IPdvService pdvService, AWSKMSKeyProvider kms, IConfiguration configuration)
            : StripeWebhookHandler(pdvService, kms, configuration)
        {
            protected override Task<string> ResolveSecretAsync(CancellationToken cancellation) =>
                Task.FromResult(WebhookSecret);
        }

        private async Task GiveChargeAsync(AppDbContext context, string intentId)
        {
            context.PdvCharge.Add(new PdvCharge
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                ClientSaleId = Guid.NewGuid(),
                Provider = "stripe",
                ProviderIntentId = intentId,
                Amount = 60m,
                Status = (int)EnumPdvChargeStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            });

            await context.SaveChangesAsync();
        }

        private static PdvService CreateService(AppDbContext context) =>
            new(context,
                new Repository<CashSession>(context),
                new Repository<CashMovement>(context),
                new Repository<PdvSale>(context),
                new Repository<PdvSalePayment>(context),
                new Mock<IOrderService<Order>>().Object,
                new Mock<IProductService<Product>>().Object,
                new Repository<PdvCharge>(context),
                PixProviderStubs.Object());
    }
}
