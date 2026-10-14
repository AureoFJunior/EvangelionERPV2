namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Write-only. No <c>Id</c> and no audit columns: the server owns both.
    /// </summary>
    public sealed class CreateEmployeeAbsenceRequestDTO
    {
        public Guid EmployeeId { get; set; }

        /// <summary>See <c>EnumEmployeeAbsenceType</c>.</summary>
        public int Type { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Omitted means "use the default for the kind": a falta is unjustified, an atestado is
        /// justified. Sending it explicitly is how HR records a rejected atestado.
        /// </summary>
        public bool? IsJustified { get; set; }

        public string? Reason { get; set; }

        /// <summary>
        /// LGPD art. 11 health data. Accepted only on an atestado, encrypted before storage, and
        /// never returned by any endpoint — see <c>EmployeeAbsenceDTO</c>.
        /// </summary>
        public string? Cid { get; set; }
    }
}
