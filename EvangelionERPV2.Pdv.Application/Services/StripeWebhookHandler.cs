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
    /// <remarks>
    /// The signature is the only authentication this path has — Stripe holds no token of ours and
    /// cannot be given one — so it is checked against the raw body, using Stripe's own
    /// <see cref="EventUtility"/> rather than a hand-rolled HMAC. Timing-safe comparison and
    /// timestamp tolerance are exactly the parts of this that are easy to get subtly wrong.
    ///
    /// It lives here rather than in the controller so it can be tested without spinning up MVC, and
    /// so the controller stays what it should be: a shim that reads a body and picks a status code.
    /// </remarks>
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
        /// <remarks>
        /// False means 400 and nothing else — no reason, no detail. A verification error that
        /// explains itself is an oracle for whoever is probing it.
        ///
        /// True covers events we deliberately ignore. Anything but a 2xx tells Stripe to retry, and
        /// retrying something we dropped on purpose is a queue that never empties.
        /// </remarks>
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
                // A secret we cannot resolve is our outage, not a bad request. Refusing is right;
                // claiming the caller's signature was wrong would be a lie in the log.
                Log.Logger.Error("Stripe webhook secret unavailable. ErrorType={ErrorType}", ex.GetType().Name);
                return false;
            }

            if (string.IsNullOrEmpty(secret))
            {
                // No secret configured means every signature would "fail" for the wrong reason.
                Log.Logger.Error("Stripe webhook secret is not configured.");
                return false;
            }

            Event stripeEvent;

            try
            {
                // throwOnApiVersionMismatch: false — a Stripe API version bump must not turn every
                // delivery into a 400 and an infinite retry queue over a payload we can still read.
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

                // Everything else is somebody else's integration on the same account.
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
        /// <remarks>
        /// Virtual purely as a seam: <c>AWSKMSKeyProvider</c> is a concrete class with non-virtual
        /// methods, and the alternative was making shared infrastructure mockable to suit one test.
        /// Overriding this is how the webhook suite signs its own payloads with a known secret and
        /// still exercises the real verification path.
        /// </remarks>
        protected virtual Task<string> ResolveSecretAsync(CancellationToken cancellation) =>
            _kmsProvider.GetKMSKeyAsync(_configuration["Stripe:WebhookSecretName"] ?? string.Empty, cancellation);
    }
}
