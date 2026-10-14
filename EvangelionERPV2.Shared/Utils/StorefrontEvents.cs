using System.Security.Cryptography;
using System.Text;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>Which ERP changes a Limen shopper is told about, and how they are signed (review spec L4).</summary>
    public static class StorefrontEvents
    {
        public const string Shipped = "order.shipped";
        public const string Refunded = "order.refunded";

        /// <summary>How long a delivery is retried before it is given up on.</summary>
        public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

        /// <summary>
        /// The event a status or tracking change on a Limen order raises, or null. Shipping it — or
        /// correcting the tracking of an order already shipped — tells the shopper once per change.
        /// </summary>
        public static string? ForUpdate(Order before, int statusAfter, string? trackingAfter)
        {
            if (before.Channel != (int)EnumOrderChannel.Storefront || statusAfter != (int)EnumOrderStatus.Shipped)
                return null;

            var newlyShipped = before.Status != (int)EnumOrderStatus.Shipped;
            var trackingChanged = !string.Equals(before.TrackingCode, trackingAfter, StringComparison.Ordinal);
            return newlyShipped || trackingChanged ? Shipped : null;
        }

        public static StorefrontEvent Create(string type, Order order, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            OrderId = order.Id,
            CheckoutId = order.ExternalReference ?? string.Empty,
            OrderNumber = StorefrontOrderNumber.For(order.Id),
            NextAttemptAt = now,
            CreatedAt = now,
            IsActive = true
        };

        /// <summary><c>sha256=</c> + the hex HMAC-SHA256 of the exact body, as Storefront.Api checks it.</summary>
        public static string Sign(string secret, string body) =>
            "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        /// <summary>30 s, 1 min, 2 min … capped at an hour.</summary>
        public static DateTime NextAttempt(int attempts, DateTime now) =>
            now + TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, attempts - 1))));
    }
}
