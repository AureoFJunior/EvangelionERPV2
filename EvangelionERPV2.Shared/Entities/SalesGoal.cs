using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// A monthly sales target, for one seller or for the whole company.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only stored part of the performance layer. Everything else on that screen — what was
    /// sold, what was received, what commission is due — is derived from orders that already exist,
    /// so storing it would create a second copy that goes stale the moment a boleto is settled.
    /// </para>
    /// <para>
    /// <see cref="UserId"/> null means the target belongs to the enterprise rather than to a person.
    /// Deliberately one amount per month rather than per product, per customer or per region: an
    /// amount is the number a small company actually manages against, and the others are a reporting
    /// cube nobody has asked for.
    /// </para>
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(Year), nameof(Month))]
    public class SalesGoal : BaseEntity
    {
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        /// <summary>The seller this target belongs to, or null for the enterprise-wide target.</summary>
        public Guid? UserId { get; set; }
        public virtual User? User { get; set; }

        public int Year { get; set; }

        /// <summary>1–12. Validated on write; a competência is a month, never a range.</summary>
        public int Month { get; set; }

        public decimal TargetAmount { get; set; }
    }
}
