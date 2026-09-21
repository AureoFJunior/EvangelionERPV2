using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// An orçamento: what a sale would cost, before anything is committed to.
    /// </summary>
    [Index(nameof(EnterpriseId), nameof(Status))]
    public class Quote : BaseEntity
    {
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        public Guid? CustomerId { get; set; }
        public virtual Customer? Customer { get; set; }

        /// <summary>Per-tenant sequential number, allocated from <see cref="QuoteNumberSequence"/>.</summary>
        public long Number { get; set; }

        public int Status { get; set; } = (int)EnumQuoteStatus.Draft;

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        /// <summary>The last day the price stands. A quote past it cannot be converted.</summary>
        public DateTime ValidUntil { get; set; } = DateTime.UtcNow.AddDays(15);

        /// <summary>Sum of the item lines, recomputed on every write — never taken from the client.</summary>
        public decimal TotalValue { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        /// <summary>
        /// The order this quote became. Filtered unique: a quote converts once, and that index is
        /// what makes a double click harmless rather than two orders for one promise.
        /// </summary>
        public Guid? ConvertedOrderId { get; set; }

        public virtual ICollection<QuoteItem>? Items { get; set; }

        /// <summary>
        /// The status as of a moment, which is the stored one unless the clock has overtaken it.
        /// </summary>
        public EnumQuoteStatus StatusAsOf(DateTime utcNow)
        {
            var stored = (EnumQuoteStatus)Status;

            if (stored is EnumQuoteStatus.Draft or EnumQuoteStatus.Sent or EnumQuoteStatus.Accepted
                && ValidUntil.Date < utcNow.Date)
            {
                return EnumQuoteStatus.Expired;
            }

            return stored;
        }
    }
}
