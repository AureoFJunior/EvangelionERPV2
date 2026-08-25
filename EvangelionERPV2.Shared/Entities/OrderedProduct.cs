using EvangelionERPV2.Shared.Enums;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public class OrderedProduct : BaseEntity
    {
        public OrderedProduct() { }

        public OrderedProduct(decimal quantity, decimal value, string unitOfMeasure, Guid orderId, Order order, Guid productId)
        {
            Quantity = quantity;
            Value = value;
            UnitOfMeasure = unitOfMeasure;
            OrderId = orderId;
            Order = order;
            ProductId = productId;
        }

        // Quantity is not money: UnitOfMeasure allows Kg/Litre/Meter, so the global
        // decimal(18,2) convention would truncate fractional units like 0.004 Kg to 0.
        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; } = 0;
        public decimal Value { get; set; } = 0;

        /// <summary>
        /// The product's CostPrice as it stood when this line was created — a snapshot, never a live
        /// join. Reading the product's current cost would rewrite the margin of every past order the
        /// next time somebody edits a price.
        /// </summary>
        /// <remarks>
        /// [JsonIgnore] because cost is Manager-tier data: order lines are serialized inside OrderDTO
        /// for every caller who can read an order, and this is the one field in them that must not
        /// be. The gated totals on OrderDTO are how cost reaches a caller entitled to see it.
        /// </remarks>
        [JsonIgnore]
        public decimal? UnitCost { get; set; } = null;

        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);

        [ForeignKey(nameof(Order))]
        public Guid? OrderId { get; set; }

        [JsonIgnore]
        public virtual Order? Order { get; set; }

        [ForeignKey(nameof(Product))]
        public Guid ProductId { get; set; }

        public virtual Product? Product { get; set; }
    }
}