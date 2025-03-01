using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.CustomerModule.Domain.Interface
{
    public interface ICustomerRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<IEnumerable<Customer>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Customer customer);
        #endregion
    }
}