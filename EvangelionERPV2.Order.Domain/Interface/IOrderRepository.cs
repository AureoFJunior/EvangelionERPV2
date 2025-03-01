using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.OrderModule.Domain.Interface
{
    public interface IOrderRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<(IEnumerable<Order>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Order order);
        #endregion
    }
}