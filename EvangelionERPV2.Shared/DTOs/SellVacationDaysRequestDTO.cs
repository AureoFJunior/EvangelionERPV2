namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>Abono pecuniário: days converted to cash under CLT art. 143.</summary>
    public sealed class SellVacationDaysRequestDTO
    {
        public Guid AccrualId { get; set; }
        public int Days { get; set; }
    }
}
