namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Moves the tenant's period lock. Null reopens everything.
    /// </summary>
    public sealed class ClosePeriodRequestDTO
    {
        public DateTime? ClosedThroughUtc { get; set; }
    }
}
