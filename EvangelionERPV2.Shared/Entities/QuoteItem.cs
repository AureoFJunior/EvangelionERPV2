using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>One line of a quote: a product, a quantity and the price offered for it.</summary>
    [Index(nameof(QuoteId))]
    public class QuoteItem : BaseEntity
    {
        public Guid QuoteId { get; set; }
        public virtual Quote? Quote { get; set; }

        public Guid ProductId { get; set; }
        public virtual Product? Product { get; set; }

        public decimal Quantity { get; set; }
        public decimal UnitValue { get; set; }
        public decimal LineAmount { get; set; }

        [MaxLength(20)]
        public string UnitOfMeasure { get; set; } = string.Empty;
    }
}
