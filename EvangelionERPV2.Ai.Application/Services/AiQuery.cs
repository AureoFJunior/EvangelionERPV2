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
    }
}
