using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Services
{
    public interface IOrderService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(Order order);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(Order order);
        public Task<string> GetOrdersBodyAsync(Enterprise? enterprise);
        Task InsertOrderInQueue(Order order);
        #endregion
    }
}