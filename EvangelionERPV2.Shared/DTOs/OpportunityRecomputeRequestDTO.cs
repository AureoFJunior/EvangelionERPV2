namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityRecomputeRequestDTO
    {
        public int HistoryWindowDays { get; set; } = 180;
        public bool OnlyOpenOpportunities { get; set; } = false;
    }
}
