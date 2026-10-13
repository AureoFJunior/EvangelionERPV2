using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// A monthly sales target, for one seller or for the whole company.
    /// </summary>
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
