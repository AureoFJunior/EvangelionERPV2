namespace EvangelionERPV2.Shared.DTOs
{
    public class PayrollEmployeeBreakdownDTO
    {
        public Guid PayableBillId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public int ReferenceYear { get; set; }
        public int ReferenceMonth { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal ThirteenthSalaryProvision { get; set; }
        public decimal VacationProvision { get; set; }
        public decimal InssAmount { get; set; }
        public decimal RatAmount { get; set; }
        public decimal ThirdPartyAmount { get; set; }
        public decimal FgtsAmount { get; set; }
        public decimal TotalCost { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsPaid { get; set; }
    }
}
