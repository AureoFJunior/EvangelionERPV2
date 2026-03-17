
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.OrderModule.Application.Interface
{
    public interface IOrderService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Delete(Guid id, Guid enterpriseId);
        void VerifyValidValues(ref Order order);
        Order Update(Order order);
        Order Update(Order order, Guid enterpriseId);
        #endregion

        #region Async
        Task<Order> CreateAsync(Order order);
        Task<Order> RefundAsync(Guid id, Guid enterpriseId, string reason);
        Task<IEnumerable<Order>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null);
        Task<Order?> GetByIdAsync(Guid id, Guid enterpriseId);
        Task<string> GetOrdersBodyAsync(Enterprise? enterprise);
        Task InsertOrderInQueue(Order order);
        #endregion
    }
}
