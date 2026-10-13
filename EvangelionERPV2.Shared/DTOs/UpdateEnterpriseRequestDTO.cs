namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The wire shape for updating an enterprise. Same omission as
    /// <see cref="CreateEnterpriseRequestDTO"/>: <c>CurrentBalance</c> is read by
    /// <c>CashFlowForecastService</c> and used to be settable by anyone holding
    /// <c>enterprise.update</c>.
    /// </summary>
    public sealed class UpdateEnterpriseRequestDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adress { get; set; } = string.Empty;
        public string? Currency { get; set; }
        public bool ShouldSendMonthlyBilling { get; set; }
        public bool ShouldSendDunning { get; set; }
    }
}
