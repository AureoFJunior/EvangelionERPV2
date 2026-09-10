using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// This repository layer signals "nothing matched" by throwing NotFoundDatabaseException rather
    /// than returning an empty list. For a tool that is a normal answer, not a failure: the model
    /// must be told zero, or it will report that the system is broken when the shop simply has none.
    /// </summary>
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
