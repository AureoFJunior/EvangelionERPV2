using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;
using Stripe;

namespace EvangelionERPV2.PdvModule.Application.Services
{
    /// <summary>
    /// Verifies a Stripe webhook and applies what it says to the charge it names.
    /// </summary>
    public class StripeWebhookHandler
    {
        private readonly IPdvService _pdvService;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private readonly IConfiguration _configuration;

        public StripeWebhookHandler(IPdvService pdvService, AWSKMSKeyProvider kmsProvider, IConfiguration configuration)
        {
            _pdvService = pdvService;
            _kmsProvider = kmsProvider;
            _configuration = configuration;
        }

        /// <summary>
        /// True when the signature held and the event was dealt with; false when it did not.
        /// </summary>
        public virtual async Task<bool> TryHandleAsync(string json, string? signatureHeader,
            CancellationToken cancellation = default)
        {
            if (string.IsNullOrWhiteSpace(signatureHeader))
                return false;

            string secret;

            try
            {
                secret = await ResolveSecretAsync(cancellation);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Stripe webhook secret unavailable. ErrorType={ErrorType}", ex.GetType().Name);
                return false;
            }

            if (string.IsNullOrEmpty(secret))
            {
                Log.Logger.Error("Stripe webhook secret is not configured.");
                return false;
            }

            Event stripeEvent;

            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, secret,
                    tolerance: 300, throwOnApiVersionMismatch: false);
            }
            catch (StripeException)
            {
                return false;
            }

            var status = stripeEvent.Type switch
            {
                "payment_intent.succeeded" => (int)EnumPdvChargeStatus.Paid,
                "payment_intent.canceled" => (int)EnumPdvChargeStatus.Cancelled,
                "payment_intent.payment_failed" => (int)EnumPdvChargeStatus.Failed,

                _ => -1
            };

            if (status < 0 || stripeEvent.Data?.Object is not PaymentIntent intent)
                return true;

            var charge = intent.LatestCharge;

            await _pdvService.ApplyChargeEventAsync(
                stripeEvent.Id,
                intent.Id,
                status,
                charge?.PaymentMethodDetails?.Pix?.BankTransactionId,

                // ponytail: only present when the balance transaction happens to be expanded on the
                // event. Fetching it costs an API call per settlement; wire that into the reconcile
                // sweep the plan already defers, where one call can cover a batch.
                charge?.BalanceTransaction == null ? null : charge.BalanceTransaction.Fee / 100m,
                status == (int)EnumPdvChargeStatus.Paid ? DateTime.UtcNow : null);

            return true;
        }

        /// <summary>
        /// The endpoint's signing secret.
        /// </summary>
        protected virtual Task<string> ResolveSecretAsync(CancellationToken cancellation) =>
            _kmsProvider.GetKMSKeyAsync(_configuration["Stripe:WebhookSecretName"] ?? string.Empty, cancellation);
    }
}
