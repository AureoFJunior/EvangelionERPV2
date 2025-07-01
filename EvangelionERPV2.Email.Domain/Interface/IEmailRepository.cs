using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EmailModule.Domain.Interface
{
    public interface IEmailRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<IEnumerable<EmailStructure>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        EmailStructure email);
        #endregion
    }
}