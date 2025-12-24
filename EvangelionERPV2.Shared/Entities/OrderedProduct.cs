using EvangelionERPV2.Shared.Enums;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public class OrderedProduct : BaseEntity
    {
        public OrderedProduct() { }

        public OrderedProduct(double quantity, double value, string unitOfMeasure, Guid orderId, Order order, Guid productId)
        {
            Quantity = quantity;
            Value = value;
            UnitOfMeasure = unitOfMeasure;
            OrderId = orderId;
            Order = order;
            ProductId = productId;
        }

        public double Quantity { get; set; } = 0;
        public double Value { get; set; } = 0;
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