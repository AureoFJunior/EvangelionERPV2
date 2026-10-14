using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Something a Limen shopper must hear about that happened in the ERP (the store shipped or refunded
    /// an order). Written in the same save as the change, then posted to Storefront.Api by
    /// <c>StorefrontEventDispatcher</c> until it answers 2xx or a day has passed — an outbox, so a change
    /// is never announced without being saved nor saved without being announced. No EnterpriseId on
    /// purpose: the dispatcher reads every tenant's.
    /// </summary>
    [Index(nameof(DeliveredAt), nameof(FailedAt), nameof(NextAttemptAt))]
    [Table("StorefrontEvent")]
    public class StorefrontEvent : BaseEntity
    {
        /// <summary><c>order.shipped</c> or <c>order.refunded</c>.</summary>
        [MaxLength(40)]
        public string Type { get; set; } = string.Empty;

        public Guid OrderId { get; set; }

        /// <summary>The Limen checkout the order belongs to (its <c>ExternalReference</c>).</summary>
        [MaxLength(64)]
        public string CheckoutId { get; set; } = string.Empty;

        [MaxLength(20)]
        public string OrderNumber { get; set; } = string.Empty;

        public int Attempts { get; set; }

        public DateTime NextAttemptAt { get; set; }

        public DateTime? DeliveredAt { get; set; }

        /// <summary>Given up on after a day of failed deliveries; logged as an error.</summary>
        public DateTime? FailedAt { get; set; }
    }
}
