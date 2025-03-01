namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class PaginatedResultDTO<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int TotalItems { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

    }
}