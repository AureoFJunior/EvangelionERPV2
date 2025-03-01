using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public class OrderedProduct : BaseEntity
    {
        public OrderedProduct() { }

        public OrderedProduct(double quantity, double value, Guid orderId, Order order, Guid productId)
        {
            Quantity = quantity;
            Value = value;
            OrderId = orderId;
            Order = order;
            ProductId = productId;
        }

        public double Quantity { get; set; } = 0;
        public double Value { get; set; } = 0;

        [ForeignKey(nameof(Order))]
        public Guid? OrderId { get; set; }

        [JsonIgnore]
        public virtual Order? Order { get; set; }

        [ForeignKey(nameof(Product))]
        public Guid ProductId { get; set; }

        [JsonIgnore]
        public virtual Product? Product { get; set; }
    }
}