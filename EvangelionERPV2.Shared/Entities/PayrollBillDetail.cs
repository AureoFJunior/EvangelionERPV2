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
    public class PayrollBillDetail : BaseEntity
    {
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
