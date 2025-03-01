using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public class Order : BaseEntity
    {
        public Order() { }

        public Order(DateTime? payday, DateTime paymentScheduledDate, double totalValue, Guid? enterpriseId, Guid? customerId, IEnumerable<OrderedProduct> orderedProduct)
        {
            Payday = payday;
            PaymentScheduledDate = paymentScheduledDate;
            TotalValue = totalValue;
            EnterpriseId = enterpriseId;
            CustomerId = customerId;
            OrderedProduct = orderedProduct;
        }

        public DateTime? Payday { get; set; } = null;
        public DateTime PaymentScheduledDate { get; set; } = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 01, 00, 00, 00).AddMonths(1);
        public double TotalValue { get; set; } = 0;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;

        [JsonIgnore]
        public virtual Enterprise? Enterprise { get; set; } = null;

        [ForeignKey(nameof(Customer))]
        public Guid? CustomerId { get; set; } = null;

        [JsonIgnore]
        public virtual Customer? Customer { get; set; } = null;

        public IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;
    }
}