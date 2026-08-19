using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(PayableBillId))]
    [Table("PayableBillProduct")]
    public class PayableBillProduct : BaseEntity
    {
        // Quantity is not money: UnitOfMeasure allows Kg/Litre/Meter, so the global
        // decimal(18,2) convention would truncate fractional units like 0.004 Kg to 0.
        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; } = 0;
        public decimal UnitValue { get; set; } = 0;
        public decimal LineAmount { get; set; } = 0;
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
