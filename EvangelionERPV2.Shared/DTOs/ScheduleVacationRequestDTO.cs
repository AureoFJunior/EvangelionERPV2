namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Books one stretch of férias against a período aquisitivo.
    /// </summary>
    /// <remarks>
    /// Length in days rather than an end date: the art. 134 §1 rules are all stated in days, and
    /// deriving the count from two dates on the wire invites an off-by-one at the boundary the law
    /// actually cares about.
    /// </remarks>
    public sealed class ScheduleVacationRequestDTO
    {
        public Guid AccrualId { get; set; }
        public DateTime StartDate { get; set; }
        public int Days { get; set; }
    }
}
