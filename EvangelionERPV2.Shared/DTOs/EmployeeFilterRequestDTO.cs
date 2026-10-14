namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class EmployeeFilterRequestDTO
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Position { get; set; }
        public string? PhoneNumber { get; set; }
        public bool? IsActive { get; set; } = true;
    }
}
