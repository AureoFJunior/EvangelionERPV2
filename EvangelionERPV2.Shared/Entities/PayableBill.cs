using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(EnterpriseId), nameof(DueDate))]
    [Table("PayableBill")]
    public class PayableBill : BaseEntity
    {
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }
        public double Amount { get; set; }
        public bool IsPaid { get; set; }
        public DateTime? ProductsReceivedAt { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        public virtual ICollection<PayableBillProduct>? Items { get; set; }
    }
}
