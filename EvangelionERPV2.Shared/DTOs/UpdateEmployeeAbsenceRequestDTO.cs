namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Write-only. <c>Id</c> is legitimate here — it names the row — but no audit column is,
    /// and neither is <c>EmployeeId</c>: an absence cannot be moved to another employee.
    /// </summary>
    public sealed class UpdateEmployeeAbsenceRequestDTO
    {
        public Guid Id { get; set; }

        /// <summary>See <c>EnumEmployeeAbsenceType</c>.</summary>
        public int Type { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>Omitted keeps the stored decision — this is the pay-impacting flag.</summary>
        public bool? IsJustified { get; set; }

        public string? Reason { get; set; }

        /// <summary>
        /// LGPD art. 11 health data. Omitted keeps the stored (encrypted) value, so a partial
        /// payload cannot wipe it; a supplied value replaces it, re-encrypted.
        /// </summary>
        public string? Cid { get; set; }
    }
}
