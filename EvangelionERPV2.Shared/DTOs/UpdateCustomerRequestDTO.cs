namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateCustomerRequestDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adress { get; set; } = string.Empty;
        public string? Document { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
