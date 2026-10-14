namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// One período aquisitivo as the API reports it.
    /// </summary>
    public sealed class EmployeeVacationAccrualDTO : BaseDTO
    {
        public Guid EmployeeId { get; set; }
        public DateTime AcquisitionStart { get; set; }
        public DateTime AcquisitionEnd { get; set; }
        public DateTime ConcessionDeadline { get; set; }
        public int EntitledDays { get; set; }
        public int ScheduledDays { get; set; }
        public int TakenDays { get; set; }
        public int SoldDays { get; set; }
        public int RemainingDays { get; set; }

        /// <summary>See <c>EnumVacationAccrualStatus</c>.</summary>
        public int Status { get; set; }

        public Guid? EnterpriseId { get; set; } = null;
    }
}
