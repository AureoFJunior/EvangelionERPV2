namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class EnterpriseDTO : BaseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adress { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
    }
}
