using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Repositories
{
    public interface IOrderRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<IEnumerable<Order>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Order order);
        #endregion
    }
}