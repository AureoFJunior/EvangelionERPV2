using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.Shared.Utils
{
    public static class PaginationExtensions
    {
        public static PaginatedResultDTO<T> ToPaginatedResult<T>(this IEnumerable<T> source, int page, int pageSize, int totalItems)
        {
            page = Math.Max(page, 1);

            return new PaginatedResultDTO<T>
            {
                Items = source,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }
    }
}
