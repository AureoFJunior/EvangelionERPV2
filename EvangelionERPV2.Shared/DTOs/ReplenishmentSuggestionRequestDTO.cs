namespace EvangelionERPV2.Shared.DTOs
{
    public class ReplenishmentSuggestionRequestDTO
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int HistoryWindowDays { get; set; } = 180;
        public bool SortByCriticality { get; set; } = true;
    }
}
