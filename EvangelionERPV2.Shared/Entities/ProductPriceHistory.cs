using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One stretch of time at one price (SPEC-limen-pricing-rules §2.2): opened when a product's
    /// <c>DefaultValue</c> is set, closed (<see cref="To"/>) when it changes. The proof a "de" price
    /// was really practised (CDC art. 37 §1).
    /// </summary>
    [Index(nameof(ProductId), nameof(From))]
    [Table("ProductPriceHistory")]
    public class ProductPriceHistory : BaseEntity
    {
        public Guid ProductId { get; set; }
        public Guid EnterpriseId { get; set; }

        public decimal Price { get; set; }

        public DateTime From { get; set; }

        /// <summary>Null while this is the product's current price.</summary>
        public DateTime? To { get; set; }
    }
}
