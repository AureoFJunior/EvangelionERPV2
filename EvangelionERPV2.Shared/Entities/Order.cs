using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public class Order : BaseEntity
    {
        public Order() { }

        public Order(DateTime? payday, DateTime paymentScheduledDate, decimal totalValue, Guid? enterpriseId, Guid? customerId, IEnumerable<OrderedProduct> orderedProduct, Guid? userId, int status = 0 )
        {
            Payday = payday;
            PaymentScheduledDate = paymentScheduledDate;
            TotalValue = totalValue;
            EnterpriseId = enterpriseId;
            CustomerId = customerId;
            OrderedProduct = orderedProduct;
            UserId = userId;
            Status = status;
        }

        public DateTime? Payday { get; set; } = null;
        public DateTime PaymentScheduledDate { get; set; } = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 01, 00, 00, 00).AddMonths(1);
        public decimal TotalValue { get; set; } = 0;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;

        [JsonIgnore]
        public virtual Enterprise? Enterprise { get; set; } = null;

        [ForeignKey(nameof(Customer))]
        public Guid? CustomerId { get; set; } = null;

        [JsonIgnore]
        public virtual Customer? Customer { get; set; } = null;

        public IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;

        [ForeignKey(nameof(User))]
        public Guid? UserId { get; set; } = null;

        public virtual User? User { get; set; } = null;

        public int Status { get; set; } = 0;

        /// <summary>
        /// How the order was settled — <see cref="Enums.EnumPaymentMethod"/>, or
        /// <see cref="Enums.EnumPaymentMethod.Mixed"/> when more than one method was used.
        /// </summary>
        public int? PaymentMethod { get; set; } = null;

        public string? RefundReason { get; set; } = null;

        public DateTime? RefundedAt { get; set; } = null;

        /// <summary>Typed by the store when it ships; shown to the shopper with <see cref="TrackingUrl"/>.</summary>
        [MaxLength(40)]
        public string? TrackingCode { get; set; } = null;

        [MaxLength(500)]
        public string? TrackingUrl { get; set; } = null;

        /// <summary><see cref="Enums.EnumOrderChannel"/>.</summary>
        public int Channel { get; set; } = 0;

        /// <summary>The Limen checkout id for a storefront order; unique per enterprise, so a replay creates nothing.</summary>
        [MaxLength(64)]
        public string? ExternalReference { get; set; } = null;

        /// <summary>Included in <see cref="TotalValue"/>.</summary>
        public decimal ShippingValue { get; set; } = 0;

        /// <summary>The delivery address as it was at checkout, as JSON.</summary>
        public string? DeliveryAddress { get; set; } = null;

        /// <summary>When the status first reached Shipped.</summary>
        public DateTime? ShippedAt { get; set; } = null;

        /// <summary>When the status first reached Delivered or Finished; the 7-day withdrawal counts from here.</summary>
        public DateTime? DeliveredAt { get; set; } = null;

        /// <summary>When the shopper exercised the 7-day withdrawal right (CDC art. 49) on the storefront.</summary>
        public DateTime? WithdrawalRequestedAt { get; set; } = null;
    }
}
