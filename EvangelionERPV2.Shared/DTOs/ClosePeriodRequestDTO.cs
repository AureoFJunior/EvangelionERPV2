namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Moves the tenant's period lock. Null reopens everything.
    /// </summary>
    /// <remarks>
    /// Its own DTO rather than a field on <see cref="UpdateEnterpriseRequestDTO"/>: closing the
    /// books is admin-only and audited, while enterprise.update is a wider permission. Binding it
    /// into the general update would hand the lock to everyone who can edit a phone number.
    /// </remarks>
    public sealed class ClosePeriodRequestDTO
    {
        public DateTime? ClosedThroughUtc { get; set; }
    }
}
