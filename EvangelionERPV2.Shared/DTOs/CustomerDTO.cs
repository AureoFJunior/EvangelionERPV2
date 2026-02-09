namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CustomerDTO : BaseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adress { get; set; } = string.Empty;
        public string? Document { get; set; }
        public Guid? EnterpriseId { get; set; } = null;
    }
}
