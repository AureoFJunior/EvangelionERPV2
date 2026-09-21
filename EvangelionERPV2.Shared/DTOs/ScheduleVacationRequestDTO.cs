namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Books one stretch of férias against a período aquisitivo.
    /// </summary>
    public sealed class ScheduleVacationRequestDTO
    {
        public Guid AccrualId { get; set; }
        public DateTime StartDate { get; set; }
        public int Days { get; set; }
    }
}
