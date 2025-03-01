namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CustomerDTO : BaseDTO
    {
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
        public string Adress { get; set; }
        public Guid? EnterpriseId { get; set; } = null;
    }
}