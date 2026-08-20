using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One row per período aquisitivo per employee: the férias entitlement earned by twelve months
    /// of work, and what has since been scheduled, taken or sold against it.
    /// </summary>
    /// <remarks>
    /// An entitlement, not an absence. <see cref="EmployeeAbsence"/> records days the employee was
    /// away; this records days they are owed. Scheduling férias writes both — an absence row so
    /// payroll stops counting those days as worked, and a decrement here.
    ///
    /// The reason this table exists at all is <see cref="ConcessionDeadline"/>. Miss it and CLT
    /// art. 137 makes the remaining days payable twice, which is a real liability accruing silently
    /// on a date nobody watches.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(EmployeeId), nameof(AcquisitionStart))]
    [Index(nameof(EnterpriseId), nameof(ConcessionDeadline))]
    public class EmployeeVacationAccrual : BaseEntity
    {
        /// <summary>First day of the 12-month período aquisitivo, inclusive.</summary>
        public DateTime AcquisitionStart { get; set; }

        /// <summary>Last day of the período aquisitivo, inclusive. Normally AcquisitionStart + 12 months - 1 day.</summary>
        public DateTime AcquisitionEnd { get; set; }

        /// <summary>
        /// <see cref="AcquisitionEnd"/> + 12 months: the last day the férias may legally start.
        /// The dobro clock (CLT art. 134, art. 137).
        /// </summary>
        public DateTime ConcessionDeadline { get; set; }

        /// <summary>
        /// Days earned, from the CLT art. 130 table applied to the unjustified faltas inside the
        /// aquisitivo: 30 days up to 5 faltas, then 24, 18, 12, and none above 32.
        /// </summary>
        public int EntitledDays { get; set; }

        /// <summary>Days on an approved future férias that has not started yet.</summary>
        public int ScheduledDays { get; set; }

        /// <summary>Days already enjoyed.</summary>
        public int TakenDays { get; set; }

        /// <summary>Abono pecuniário (CLT art. 143): days converted to cash, capped at a third of the entitlement.</summary>
        public int SoldDays { get; set; }

        /// <summary>See <c>EnumVacationAccrualStatus</c>. Stored as int so new states need no migration.</summary>
        public int Status { get; set; }

        /// <summary>
        /// Days still unaccounted for. Not mapped: it is arithmetic over three columns, and storing
        /// it would create a fourth value that can disagree with them.
        /// </summary>
        [NotMapped]
        public int RemainingDays => EntitledDays - ScheduledDays - TakenDays - SoldDays;

        [ForeignKey(nameof(Employee))]
        public Guid EmployeeId { get; set; }
        public virtual Employee? Employee { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;
    }
}
