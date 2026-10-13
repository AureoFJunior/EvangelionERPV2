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
        public const int ExpiresAfterSeconds = 30 * 60;

        /// <summary>
        /// Raises a charge on <paramref name="connectedAccountId"/>'s behalf.
        /// </summary>
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
