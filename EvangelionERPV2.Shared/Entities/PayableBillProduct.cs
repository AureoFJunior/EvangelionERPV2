using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(PayableBillId), nameof(ProductId), IsUnique = true)]
    [Index(nameof(PayableBillId))]
    [Table("PayableBillProduct")]
    public class PayableBillProduct : BaseEntity
    {
        public double Quantity { get; set; } = 0;
        public double UnitValue { get; set; } = 0;
        public double LineAmount { get; set; } = 0;
        public string UnitOfMeasure { get; set; } = string.Empty;

        [ForeignKey(nameof(PayableBill))]
        public Guid PayableBillId { get; set; }

        [JsonIgnore]
        public virtual PayableBill? PayableBill { get; set; }

        [ForeignKey(nameof(Product))]
        public Guid ProductId { get; set; }

        public virtual Product? Product { get; set; }
    }
}
