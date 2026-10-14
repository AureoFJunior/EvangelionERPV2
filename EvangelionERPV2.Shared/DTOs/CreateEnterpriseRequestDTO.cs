namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The wire shape for creating an enterprise. It carries no <c>CurrentBalance</c> and no audit
    /// columns on purpose: the balance is money the system computes, so it must not be reachable
    /// from a request body.
    /// </summary>
    public sealed class CreateEnterpriseRequestDTO
    {
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adress { get; set; } = string.Empty;
        public string? Currency { get; set; }
        public bool ShouldSendMonthlyBilling { get; set; }
        public bool ShouldSendDunning { get; set; }
    }
}
