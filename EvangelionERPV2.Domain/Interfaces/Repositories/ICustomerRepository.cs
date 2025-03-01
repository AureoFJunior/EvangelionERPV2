
using EvangelionERPV2.Domain.Models;
using System.Linq.Expressions;

namespace EvangelionERPV2.Domain.Interfaces.Repositories
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