namespace EvangelionERPV2.Shared.DTOs
{
    public class EnterprisePayrollSettingsDTO
    {
        public decimal InssRate { get; set; } = 0.20m;
        public decimal RatRate { get; set; } = 0.02m;
        public decimal ThirdPartyRate { get; set; } = 0.058m;
        public decimal FgtsRate { get; set; } = 0.08m;
        public int PayrollDueDay { get; set; } = 5;
    }
}
