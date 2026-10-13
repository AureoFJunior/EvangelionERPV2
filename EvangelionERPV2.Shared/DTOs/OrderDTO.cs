using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderDTO : BaseDTO
    {
        public DateTime? Payday { get; set; } = null;
        public DateTime? PaymentScheduledDate { get; set; } = null;
        public decimal TotalValue { get; set; } = 0;

        public decimal? TotalCost { get; set; } = null;
        public decimal? MarginAmount { get; set; } = null;
        public decimal? MarginPercent { get; set; } = null;

        public Guid? EnterpriseId { get; set; } = null;
        public Guid? CustomerId { get; set; } = null;
        public string? CustomerName { get; set; } = null;
        public IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;
        public int Status { get; set; } = 0;

        public int? PaymentMethod { get; set; } = null;

        public string? RefundReason { get; set; } = null;
        public DateTime? RefundedAt { get; set; } = null;

        public string? TrackingCode { get; set; } = null;
        public string? TrackingUrl { get; set; } = null;
        public DateTime? WithdrawalRequestedAt { get; set; } = null;

        /// <summary><see cref="Enums.EnumOrderChannel"/>: Erp, Pdv or Storefront (Limen).</summary>
        public int Channel { get; set; } = 0;

        /// <summary>Part of <see cref="TotalValue"/>.</summary>
        public decimal ShippingValue { get; set; } = 0;

        /// <summary>Where a Limen order goes, as typed at checkout; null for other channels.</summary>
        public OrderDeliveryAddressDTO? DeliveryAddress { get; set; } = null;

        /// <summary>"LM-XXXXXXXX", the number the Limen shopper sees; null for other channels.</summary>
        public string? StorefrontOrderNumber { get; set; } = null;

        public DateTime? ShippedAt { get; set; } = null;
        public DateTime? DeliveredAt { get; set; } = null;
    }

    public sealed class OrderDeliveryAddressDTO
    {
        public string Cep { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string? Number { get; set; }
        public string? Complement { get; set; }
        public string District { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;

        /// <summary>The address stored on the order, or null when it has none or it cannot be read.</summary>
        public static OrderDeliveryAddressDTO? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<OrderDeliveryAddressDTO>(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
