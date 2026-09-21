using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    // The EnterpriseId unique filtered index is declared fluently in AppDbContext —
    // [Index] cannot express HasFilter.
    [Table("EnterprisePayrollSettings")]
    public class EnterprisePayrollSettings : BaseEntity
    {
        [Column(TypeName = "decimal(5,4)")]
        public decimal InssRate { get; set; } = 0.20m;

        [Column(TypeName = "decimal(5,4)")]
        public decimal RatRate { get; set; } = 0.02m;

        [Column(TypeName = "decimal(5,4)")]
        public decimal ThirdPartyRate { get; set; } = 0.058m;

        [Column(TypeName = "decimal(5,4)")]
        public decimal FgtsRate { get; set; } = 0.08m;

        public int PayrollDueDay { get; set; } = 5;

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }
    }
}
