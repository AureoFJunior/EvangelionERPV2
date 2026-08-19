namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateEmployeeRequestDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string? Document { get; set; }
        public decimal? Salary { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
