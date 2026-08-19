namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CreateEmployeeRequestDTO
    {
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string? Document { get; set; }
        public decimal? Salary { get; set; }
        public DateTime? HireDate { get; set; }
        public bool? IsActive { get; set; } = true;

        // Optional base64 image payload uploaded to S3 after the employee is created.
        public string? Picture { get; set; }
    }
}
