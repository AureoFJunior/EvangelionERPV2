using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    // One row per employee per payroll competência month. Persists the full split of the
    // employer cost so payroll payables stay auditable after rates change.
    // LGPD: carries salary-derived values — only exposed through payroll endpoints.
    // The (ReferenceYear, ReferenceMonth, EmployeeId) unique filtered index is declared
    // fluently in AppDbContext — [Index] cannot express HasFilter.
    [Table("PayrollBillDetail")]
    [Index(nameof(EnterpriseId), nameof(ReferenceYear), nameof(ReferenceMonth))]
    public class PayrollBillDetail : BaseEntity
    {
        /// <summary>
        /// The tenant this row belongs to.
        /// </summary>
        /// <remarks>
        /// It had none until 2026-09-01, and isolation came from post-filtering: every query loaded
        /// the competência for *all* tenants and then discarded the rows whose payable bill was not
        /// this enterprise's. The answers were right and nothing leaked, but salary-derived rows of
        /// every other company were read into memory to produce them, and the next query written
        /// over this table would have had to remember the join or leak. With the column the
        /// convention filter in <c>AppDbContext.ApplyTenantQueryFilters</c> covers it like every
        /// other tenant-owned entity, and the hand-written predicates stay as the second belt.
        ///
        /// A bare Guid, not a navigation: this row already reaches Enterprise through both its
        /// payable bill and its employee, and a third FK is a third cascade path SQL Server
        /// refuses. <c>PdvSalePayment</c> carries its tenant the same way.
        /// </remarks>
        public Guid EnterpriseId { get; set; }

        public int ReferenceYear { get; set; }
        public int ReferenceMonth { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GrossSalary { get; set; }

        // 13º salário accrual (1/12 of gross).
        [Column(TypeName = "decimal(18,2)")]
        public decimal ThirteenthSalaryProvision { get; set; }

        // Férias accrual including the 1/3 constitutional bonus (1/12 * 4/3 of gross).
        [Column(TypeName = "decimal(18,2)")]
        public decimal VacationProvision { get; set; }

        /// <summary>
        /// How much of this month's <see cref="VacationProvision"/> a férias payment has already
        /// drawn down.
        /// </summary>
        /// <remarks>
        /// The provision accrues monthly and the férias bill consumes it, so the forecast must read
        /// the remainder rather than the accrual — otherwise the same money is counted twice, once
        /// as a provision still owed and once as cash already going out. Stored per competência
        /// rather than as one running total on the accrual because a split férias draws on several
        /// months and the allocation has to stay auditable after the fact.
        /// </remarks>
        [Column(TypeName = "decimal(18,2)")]
        public decimal VacationProvisionConsumed { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal InssAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal RatAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ThirdPartyAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FgtsAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        [ForeignKey(nameof(PayableBill))]
        public Guid PayableBillId { get; set; }
        public virtual PayableBill? PayableBill { get; set; }

        [ForeignKey(nameof(Employee))]
        public Guid EmployeeId { get; set; }
        public virtual Employee? Employee { get; set; }
    }
}
