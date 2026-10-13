using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// The per-tenant quote counter, incremented by one atomic statement.
    /// </summary>
    [Index(nameof(EnterpriseId), IsUnique = true)]
    public class QuoteNumberSequence : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>Next number to hand out. Incremented in place by the allocation statement.</summary>
        public long NextNumber { get; set; } = 1;
    }
}
