using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One Limen purchase as the ERP priced it: the only source orders are built from, never the
    /// browser. Spans several enterprises, so it carries no <c>EnterpriseId</c> and no tenant filter;
    /// only the storefront-scoped endpoints read it. SPEC-ecommerce-storefront.md §4.2, §14.
    /// </summary>
    [Index(nameof(ShopperId))]
    [Index(nameof(Email))]
    [Table("StorefrontCheckout")]
    public class StorefrontCheckout : BaseEntity
    {
        /// <summary>The Storefront.Api account that owns it; null for a guest or a deleted account.</summary>
        public Guid? ShopperId { get; set; }

        [MaxLength(254)]
        public string Email { get; set; } = string.Empty;

        /// <summary>Storefront <c>CheckoutStatus</c>: AwaitingPayment, Paid, Declined, Expired, Settling.</summary>
        public int Status { get; set; }

        /// <summary>Storefront <c>PaymentMethod</c>: Card, Pix.</summary>
        public int PaymentMethod { get; set; }

        public decimal TotalValue { get; set; }

        /// <summary>Card instalments (1 for Pix and cash-price card). The orders keep cash amounts; the extra is interest.</summary>
        public int Installments { get; set; } = 1;
        public decimal? InstallmentValue { get; set; }

        /// <summary>Percent a month and CET a year, as shown to the shopper when paying (CDC art. 52).</summary>
        public decimal? InterestMonthly { get; set; }
        public decimal? CetAnnual { get; set; }

        /// <summary>What the card is charged: <see cref="TotalValue"/> plus interest.</summary>
        public decimal? TotalWithInterest { get; set; }

        public string QuoteJson { get; set; } = string.Empty;

        /// <summary>Name, e-mail, CPF, phone and marketing opt-in as typed at checkout.</summary>
        public string CustomerJson { get; set; } = string.Empty;

        public string AddressJson { get; set; } = string.Empty;

        /// <summary>Per store: its order, or its share refunded for lack of stock.</summary>
        public string OutcomesJson { get; set; } = "[]";

        [MaxLength(300)]
        public string? PixCode { get; set; }

        public DateTime? PixExpiresAt { get; set; }

        [MaxLength(40)]
        public string? DeclineCode { get; set; }

        public DateTime? PaidAt { get; set; }

        /// <summary>How many times the orders of this paid checkout were attempted.</summary>
        public int SettleAttempts { get; set; }

        /// <summary>No money moved: the test-mode simulator settled it.</summary>
        public bool Simulated { get; set; }
    }
}
