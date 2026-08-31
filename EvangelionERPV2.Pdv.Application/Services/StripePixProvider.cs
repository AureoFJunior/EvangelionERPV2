using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Stripe;

namespace EvangelionERPV2.PdvModule.Application.Services
{
    /// <summary>What a provider tells us back about a charge it just raised.</summary>
    public sealed record PixIntent(string IntentId, string QrPayload, DateTime ExpiresAt, string Status);

    /// <summary>
    /// Raises, reads and cancels a PIX PaymentIntent on a shop's own Stripe account.
    /// </summary>
    /// <remarks>
    /// A translation layer and nothing else. It knows about Stripe and about money, and knows
    /// nothing about <c>PdvCharge</c>, tenancy or HTTP — so the rules that matter (which shop may
    /// charge, for how much, and only once per basket) live in <see cref="PdvService"/> where they
    /// can be tested without a network.
    ///
    /// The secret key is resolved per call from AWS Secrets Manager and handed to Stripe.net as a
    /// <see cref="RequestOptions.ApiKey"/>. It is never stored on this instance, never written to
    /// the database and never logged: the constraint this whole design exists to hold is that no
    /// key is reachable from a shop floor, and a field on a scoped service is one heap dump away
    /// from being reachable.
    ///
    /// Methods are virtual so the service's tests can stand in for them. Stubbing Stripe's own
    /// <c>IStripeClient</c> would mean canned JSON for a layer that is three property assignments
    /// wide; the assertions worth writing are about our gating and our rows, and those are on the
    /// far side of this seam. What only a real call can prove is proved in Task 10.
    /// </remarks>
    public class StripePixProvider
    {
        private readonly AWSKMSKeyProvider _kmsProvider;
        private readonly IConfiguration _configuration;

        public StripePixProvider(AWSKMSKeyProvider kmsProvider, IConfiguration configuration)
        {
            _kmsProvider = kmsProvider;
            _configuration = configuration;
        }

        /// <summary>How long a customer has to open their bank app and pay.</summary>
        /// <remarks>
        /// A calibration knob, not a constant of nature: long enough that a queue does not time out
        /// mid-payment, short enough that an abandoned basket frees up. Tune it against a real shop.
        /// </remarks>
        public const int ExpiresAfterSeconds = 30 * 60;

        /// <summary>
        /// Raises a charge on <paramref name="connectedAccountId"/>'s behalf.
        /// </summary>
        /// <remarks>
        /// <c>OnBehalfOf</c> makes the shop the merchant of record, which is not a preference:
        /// NFC-e requires the CNPJ on the fiscal document and the CNPJ receiving the money to match.
        ///
        /// The idempotency key is the basket id, so a create retried after a lost response cannot
        /// open a second intent at Stripe either — the same guarantee our own unique index gives on
        /// this side, asserted at both ends because a customer who pays twice is the worst outcome
        /// this code has.
        /// </remarks>
        public virtual async Task<PixIntent> CreateAsync(
            decimal amount, Guid clientSaleId, string connectedAccountId, CancellationToken cancellation = default)
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = ToCentavos(amount),
                Currency = "brl",
                PaymentMethodTypes = ["pix"],
                PaymentMethodData = new PaymentIntentPaymentMethodDataOptions { Type = "pix" },
                PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
                {
                    Pix = new PaymentIntentPaymentMethodOptionsPixOptions
                    {
                        ExpiresAfterSeconds = ExpiresAfterSeconds
                    }
                },
                OnBehalfOf = connectedAccountId,

                // Confirmed on creation: an unconfirmed intent has no QR, and a QR is the entire
                // point of the call.
                Confirm = true,
                Metadata = new Dictionary<string, string> { ["client_sale_id"] = clientSaleId.ToString() }
            };

            var requestOptions = await RequestOptionsAsync(cancellation);
            requestOptions.IdempotencyKey = clientSaleId.ToString();

            var intent = await new PaymentIntentService().CreateAsync(options, requestOptions, cancellation);
            var qr = intent.NextAction?.PixDisplayQrCode;

            return new PixIntent(
                intent.Id,
                qr?.Data ?? string.Empty,
                qr?.ExpiresAt ?? DateTime.UtcNow.AddSeconds(ExpiresAfterSeconds),
                intent.Status);
        }

        /// <summary>Cancels an intent nobody paid. Stripe refuses one that already succeeded.</summary>
        public virtual async Task CancelAsync(string intentId, CancellationToken cancellation = default)
        {
            await new PaymentIntentService().CancelAsync(
                intentId, new PaymentIntentCancelOptions { CancellationReason = "abandoned" },
                await RequestOptionsAsync(cancellation), cancellation);
        }

        /// <summary>
        /// Centavos, rounded away from zero.
        /// </summary>
        /// <remarks>
        /// Not <c>decimal.Round</c>: its default is banker's rounding, which sends R$ 0,125 down to
        /// 12 centavos and disagrees with every total the till computed with <c>Money.Round</c>.
        /// A charge that differs from the basket by a centavo is a sale the books cannot close.
        /// </remarks>
        internal static long ToCentavos(decimal amount) =>
            (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

        private async Task<RequestOptions> RequestOptionsAsync(CancellationToken cancellation)
        {
            var secretName = _configuration["Stripe:SecretKeyName"] ?? string.Empty;

            return new RequestOptions
            {
                ApiKey = await _kmsProvider.GetKMSKeyAsync(secretName, cancellation)
            };
        }
    }
}
