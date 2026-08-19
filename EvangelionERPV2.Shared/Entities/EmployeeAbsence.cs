using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One row per absence event: falta, atestado, or advertência, told apart by <see cref="Type"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately one table and not three. Payroll needs the worked-day count for a whole
    /// enterprise in one round trip, and every kind shares the same shape (an employee, a day
    /// range, whether it is justified); only the atestado adds a field of its own.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(EmployeeId), nameof(StartDate))]
    public class EmployeeAbsence : BaseEntity
    {
        /// <summary>See <c>EnumEmployeeAbsenceType</c>. Stored as int so new kinds do not need a migration.</summary>
        public int Type { get; set; }

        /// <summary>First calendar day covered, inclusive.</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Last calendar day covered, inclusive. Equal to <see cref="StartDate"/> for a single day.</summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Drives pay: only an unjustified absence reduces the worked days payroll bills for.
        /// A falta defaults to false, an atestado to true — but it stays a stored decision, not a
        /// property of the type, because a rejected atestado is a falta and HR has to be able to
        /// say so.
        /// </summary>
        public bool IsJustified { get; set; }

        /// <summary>Free-text note. Never a diagnosis — that is <see cref="Cid"/>.</summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// LGPD art. 11 special-category health data: the CID (diagnosis code) on an atestado.
        /// Encrypted at rest with <c>SharedFunctions.Encrypt</c> before it is ever handed to the
        /// repository, redacted out of the audit trail, and absent from every response DTO — the
        /// API takes it in and never gives it back.
        /// </summary>
        public string? Cid { get; set; } = null;

        [ForeignKey(nameof(Employee))]
        public Guid EmployeeId { get; set; }
        public virtual Employee? Employee { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;
    }
}
