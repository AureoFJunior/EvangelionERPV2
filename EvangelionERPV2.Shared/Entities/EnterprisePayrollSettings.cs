using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    // The EnterpriseId unique filtered index is declared fluently in AppDbContext —
    // [Index] cannot express HasFilter.
    [Table("EnterprisePayrollSettings")]
    public class EnterprisePayrollSettings : BaseEntity
    {
        // Employer charge rates stored as fractions of gross salary. Defaults follow the
        // Brazilian Lucro Presumido/Real regime: INSS patronal 20%, RAT 2%,
        // Terceiros/Sistema S 5.8%, FGTS 8%. Simples Nacional enterprises can zero the
        // INSS/RAT/Terceiros rates.
        [Column(TypeName = "decimal(5,4)")]
        public decimal InssRate { get; set; } = 0.20m;

        [Column(TypeName = "decimal(5,4)")]
        public decimal RatRate { get; set; } = 0.02m;

        [Column(TypeName = "decimal(5,4)")]
        public decimal ThirdPartyRate { get; set; } = 0.058m;

        [Column(TypeName = "decimal(5,4)")]
        public decimal FgtsRate { get; set; } = 0.08m;

        // Day of the following month when the payroll payable is due (CLT: até o 5º dia útil).
        public int PayrollDueDay { get; set; } = 5;

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }
    }
}
