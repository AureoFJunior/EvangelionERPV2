using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Shared.Enums;
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
        public decimal Amount { get; set; }
        public bool IsPaid { get; set; }
        public int BillType { get; set; } = (int)EnumPayableBillType.Other;
        public DateTime? ProductsReceivedAt { get; set; } = null;
        public string? RefundReason { get; set; } = null;
        public DateTime? RefundedAt { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        // Optional: payables predate suppliers, and payroll or tax bills have no counterparty
        // at all, so the FK stays nullable rather than forcing a placeholder supplier.
        [ForeignKey(nameof(Supplier))]
        public Guid? SupplierId { get; set; } = null;
        public virtual Supplier? Supplier { get; set; } = null;

        /// <summary>
        /// Set only on a <c>BillType = Vacation</c> bill: the período aquisitivo it pays out.
        /// </summary>
        /// <remarks>
        /// A direct FK rather than a side table, because a férias bill pays exactly one accrual and
        /// the link is what makes the provision drawdown auditable — without it, "which months did
        /// this payment consume" is a guess. Nullable and Restrict for the same reasons
        /// <see cref="SupplierId"/> is: every other kind of payable has no accrual at all, and an
        /// accrual that was paid out must outlive any attempt to hard-delete it.
        /// </remarks>
        [ForeignKey(nameof(EmployeeVacationAccrual))]
        public Guid? EmployeeVacationAccrualId { get; set; } = null;
        public virtual EmployeeVacationAccrual? EmployeeVacationAccrual { get; set; } = null;

        /// <summary>
        /// What a <c>BillType = Vacation</c> bill is worth before the provision already accrued for
        /// it is netted out: salary + 1/3 + abono, as the employee is actually paid it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="Amount"/> on a férias bill carries the <em>netted</em> figure, because the
        /// monthly payroll bill already projected the provision as cash leaving — see
        /// docs/employee/SPEC-hr-module.md §4 P5a. Without the gross stored, the drawdown would sum
        /// netted amounts and allocate against its own output.
        /// </para>
        /// <para>
        /// Nullable: every other kind of payable has no gross distinct from its amount, and bills
        /// issued before this column existed read as "gross equals amount", which is what they are.
        /// </para>
        /// </remarks>
        public decimal? GrossAmount { get; set; } = null;

        public virtual ICollection<PayableBillProduct>? Items { get; set; }
    }
}
