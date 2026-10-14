namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityFilterDTO
    {
        public string? Type { get; set; }
        public string? Status { get; set; }
        public double? MinScore { get; set; }
        public double? MaxScore { get; set; }
        public double? MinImpact { get; set; }
        public double? MaxImpact { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string SortBy { get; set; } = "priority";
        public bool Descending { get; set; } = true;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}
