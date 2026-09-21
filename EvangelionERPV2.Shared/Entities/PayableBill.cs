using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Shared.Enums;
using System.ComponentModel.DataAnnotations;
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

        /// <summary>
        /// The 44-digit chave of the supplier NF-e this payable was imported from, when it was.
        /// </summary>
        [MaxLength(44)]
        public string? SupplierNFeAccessKey { get; set; } = null;

        /// <summary>
        /// The duplicata (instalment) of that nota this bill pays, when the nota carried a schedule.
        /// </summary>
        [MaxLength(60)]
        public string? SupplierNFeDuplicata { get; set; } = null;

        /// <summary>
        /// How many months apart this bill repeats. Null — the default — means it does not.
        /// </summary>
        public int? RecurrenceMonths { get; set; } = null;

        /// <summary>The last due date the chain may reach. Required when the bill repeats.</summary>
        public DateTime? RecurrenceUntilUtc { get; set; } = null;

        /// <summary>
        /// The bill this one was cloned from, when it was generated rather than typed.
        /// </summary>
        public Guid? RecurrenceParentId { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        [ForeignKey(nameof(Supplier))]
        public Guid? SupplierId { get; set; } = null;
        public virtual Supplier? Supplier { get; set; } = null;

        /// <summary>
        /// Set only on a <c>BillType = Vacation</c> bill: the período aquisitivo it pays out.
        /// </summary>
        [ForeignKey(nameof(EmployeeVacationAccrual))]
        public Guid? EmployeeVacationAccrualId { get; set; } = null;
        public virtual EmployeeVacationAccrual? EmployeeVacationAccrual { get; set; } = null;

        /// <summary>
        /// What a <c>BillType = Vacation</c> bill is worth before the provision already accrued for
        /// it is netted out: salary + 1/3 + abono, as the employee is actually paid it.
        /// </summary>
        public decimal? GrossAmount { get; set; } = null;

        /// <summary>
        /// Abono pecuniário days settled by a <c>BillType = Vacation</c> bill (CLT art. 143).
        /// </summary>
        public int? AbonoDaysSettled { get; set; } = null;

        public virtual ICollection<PayableBillProduct>? Items { get; set; }
    }
}
