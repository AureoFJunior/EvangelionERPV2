namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CustomerFilterRequestDTO
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Document { get; set; }
        public string? PhoneNumber { get; set; }
        public bool? IsActive { get; set; } = true;
    }
}
