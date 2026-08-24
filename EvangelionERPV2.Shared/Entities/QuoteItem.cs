using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>One line of a quote: a product, a quantity and the price offered for it.</summary>
    /// <remarks>
    /// <see cref="UnitValue"/> is the quoted price, which is deliberately allowed to differ from
    /// <c>Product.DefaultValue</c> — quoting a discount is most of the point — and it is what the
    /// converted order carries, so the customer is charged what they were promised.
    /// </remarks>
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
