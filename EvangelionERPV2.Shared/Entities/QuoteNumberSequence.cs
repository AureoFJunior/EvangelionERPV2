using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// The per-tenant quote counter, incremented by one atomic statement.
    /// </summary>
    /// <remarks>
    /// Same shape as <see cref="BillNumberSequence"/> and <c>NFeNumberSequence</c>: a row per
    /// tenant and an <c>UPDATE ... OUTPUT deleted</c> that hands out a number without a read first,
    /// so two salespeople saving at the same moment cannot both get quote 41. A quote number is not
    /// fiscal — nothing rejects a gap — but two documents with the same number in front of two
    /// customers is a conversation nobody wants to have.
    /// </remarks>
    [Index(nameof(EnterpriseId), IsUnique = true)]
    public class QuoteNumberSequence : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>Next number to hand out. Incremented in place by the allocation statement.</summary>
        public long NextNumber { get; set; } = 1;
    }
}
