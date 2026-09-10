using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.AiModule.Application.Services
{
    public static class AiQuery
    {
        public static async Task<IReadOnlyList<T>> OrEmptyAsync<T>(Func<Task<IEnumerable<T>>> query)
        {
            try
            {
                return (await query()).ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return Array.Empty<T>();
            }
        }

        /// <summary>
        /// One row by id, or null. <c>GetByIdAsync</c> throws when the row is missing, which turns a
        /// plain "não encontrado" into an unhandled failure at the call site.
        /// </summary>
        public static async Task<T?> ByIdOrNullAsync<T>(Func<Task<T>> query) where T : class
        {
            try
            {
                return await query();
            }
            catch (NotFoundDatabaseException)
            {
                return null;
            }
        }

        /// <summary>Same for a paged read: nothing found is an empty page, not an error.</summary>
        public static async Task<(IReadOnlyList<T> Items, int Total)> OrEmptyPageAsync<T>(
            Func<Task<(IReadOnlyList<T> Items, int TotalItems)>> query)
        {
            try
            {
                var page = await query();
                return (page.Items, page.TotalItems);
            }
            catch (NotFoundDatabaseException)
            {
                return (Array.Empty<T>(), 0);
            }
        }
    }
}
