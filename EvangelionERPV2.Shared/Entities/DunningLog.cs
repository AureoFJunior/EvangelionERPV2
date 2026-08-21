using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One reminder that was sent for one receivable at one point on the régua.
    /// </summary>
    /// <remarks>
    /// This table is the idempotency, not a report. The unique index on
    /// (EnterpriseId, OrderId, OffsetDays) is what stops a second sweep in the same day — or a
    /// replica that woke a second late — from mailing a customer twice about the same debt.
    ///
    /// Keyed on the Order and not on the Bill because the order is the receivable: BillsSettings
    /// is disabled in production, so a régua that could only chase boletos would chase nothing.
    ///
    /// Deliberately NOT filtered on IsActive, for the same reason BankStatementLine's key is not:
    /// a soft-deleted row that let the sweeper re-send is the exact failure the key exists to
    /// prevent.
    ///
    /// It carries its own EnterpriseId so the tenant filter applies to it directly.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(OrderId), nameof(OffsetDays), IsUnique = true)]
    [Table("DunningLog")]
    public class DunningLog : BaseEntity
    {
        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        [ForeignKey(nameof(Order))]
        public Guid OrderId { get; set; }
        public virtual Order? Order { get; set; }

        /// <summary>
        /// Where on the régua this reminder sat, signed and relative to the due date: -3 is three
        /// days before, 0 is the due date, 7 is a week late.
        /// </summary>
        public int OffsetDays { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [MaxLength(254)]
        public string RecipientEmail { get; set; } = string.Empty;
    }
}
