namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The only shape an absence leaves the API in — list and detail alike.
    /// </summary>
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
