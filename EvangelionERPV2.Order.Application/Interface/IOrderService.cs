
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.OrderModule.Application.Interface
{
    public interface IOrderService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        void VerifyValidValues(ref Order order);
        Order Update(Order order);
        #endregion

        #region Async
        Task<Order> CreateAsync(Order order);
        Task<IList<TEntity>> GetMonthlyBillingOrders(Enterprise enterprise);
        Task<string> GetOrdersBodyAsync(Enterprise? enterprise);
        Task InsertOrderInQueue(Order order);
        #endregion
    }
}