namespace EvangelionERPV2.Domain.DTOs
{
    public class EnterpriseDTO
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool? IsActive { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
        public string Adress { get; set; }
        public bool ShouldSendMonthlyBilling { get; set; }
    }
}