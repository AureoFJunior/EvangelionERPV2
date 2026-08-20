namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// One férias payment: the stretch it covers, how it breaks down, and the bill that pays it.
    /// </summary>
    /// <remarks>
    /// LGPD: carries a salary-derived amount per named employee, so it is only ever returned from
    /// the admin-only payroll surface.
    /// </remarks>
    public class VacationPayDTO
    {
        public Guid PayableBillId { get; set; }
        public Guid EmployeeVacationAccrualId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>First and last day of the stretch, inclusive.</summary>
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public int Days { get; set; }

        /// <summary>Abono pecuniário days settled with this payment, usually zero.</summary>
        public int SoldDays { get; set; }

        public decimal SalaryPortion { get; set; }
        public decimal ConstitutionalThird { get; set; }
        public decimal AbonoAmount { get; set; }
        public decimal AbonoConstitutionalThird { get; set; }
        public decimal TotalAmount { get; set; }

        /// <summary>Two days before <see cref="StartDate"/> — CLT art. 145.</summary>
        public DateTime DueDate { get; set; }

        public bool IsPaid { get; set; }
    }
}
