namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The only shape an absence leaves the API in — list and detail alike.
    /// </summary>
    /// <remarks>
    /// LGPD: there is no <c>Cid</c> property here on purpose, not a <c>[JsonIgnore]</c>d one.
    /// The diagnosis code is art. 11 special-category health data with no read use case in this
    /// system: payroll only needs <c>IsJustified</c>, and HR only needs to know an atestado exists.
    /// A field that does not exist cannot be leaked by a future mapper, a serializer setting, or a
    /// projection someone writes in a hurry.
    /// </remarks>
    public sealed class EmployeeAbsenceDTO : BaseDTO
    {
        public Guid EmployeeId { get; set; }
        public int Type { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsJustified { get; set; }
        public string? Reason { get; set; }
        public Guid? EnterpriseId { get; set; } = null;
    }
}
