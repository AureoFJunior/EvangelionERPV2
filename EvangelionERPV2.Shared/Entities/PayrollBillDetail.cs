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
        public Guid EnterpriseId { get; set; }

        public int ReferenceYear { get; set; }
        public int ReferenceMonth { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GrossSalary { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ThirteenthSalaryProvision { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal VacationProvision { get; set; }

        /// <summary>
        /// How much of this month's <see cref="VacationProvision"/> a férias payment has already
        /// drawn down.
        /// </summary>
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
