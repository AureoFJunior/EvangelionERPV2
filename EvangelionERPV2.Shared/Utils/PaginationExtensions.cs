using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.Shared.Utils
{
    public static class PaginationExtensions
    {
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;

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

        public static (int PageNumber, int PageSize) NormalizePagination(int? pageNumber, int? pageSize, int? maxPageSize = null)
        {
            var normalizedPageNumber = pageNumber.GetValueOrDefault(DefaultPageNumber);
            if (normalizedPageNumber <= 0)
                normalizedPageNumber = DefaultPageNumber;

            var normalizedPageSize = pageSize.GetValueOrDefault(DefaultPageSize);
            if (normalizedPageSize <= 0)
                normalizedPageSize = DefaultPageSize;

            var effectiveMaxPageSize = maxPageSize ?? MaxPageSize;
            if (normalizedPageSize > effectiveMaxPageSize)
                normalizedPageSize = effectiveMaxPageSize;

            return (normalizedPageNumber, normalizedPageSize);
        }
    }
}
