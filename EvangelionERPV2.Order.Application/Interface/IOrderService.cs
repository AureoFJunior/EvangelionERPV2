
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.OrderModule.Application.Interface
{
    public sealed record OrderUpdateResult(Order Order, bool ShipmentTransitionClaimed, bool TrackingCorrectionClaimed = false);

    public interface IOrderService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Delete(Guid id, Guid enterpriseId);
        void VerifyValidValues(ref Order order);
        Order Update(Order order);
        Order Update(Order order, Guid enterpriseId);
        OrderUpdateResult UpdateWithShipmentClaim(Order order, Guid enterpriseId);
        #endregion

        #region Async
        /// <param name="allowCreditOverride">
        /// Skips the customer's credit limit. Defaulted, so the RabbitMQ path enforces it and only
        /// a controller that checked customers.credit.override can turn it off.
        /// </param>
        /// <param name="allowNegativeStock">
        /// Lets the sale post even when stock says there is not enough. Only the PDV offline sync
        /// sets it: that sale already happened at the till, so refusing it here would lose the
        /// revenue without putting the goods back on the shelf.
        /// </param>
        Task<Order> CreateAsync(Order order, bool allowCreditOverride = false, bool allowNegativeStock = false, string? messageId = null);
        Task<Order> RefundAsync(Guid id, Guid enterpriseId, string reason);
        Task<IEnumerable<Order>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null);
        /// <summary>
        /// Applies the filter, the sort and the page in SQL. Unlike
        /// <see cref="GetByEnterpriseIdAsync"/> the result size is bounded by <paramref name="pageSize"/>,
        /// so a tenant with a large order history costs the same as a small one.
        /// </summary>
        Task<IEnumerable<Order>> GetByFilterAsync(Guid enterpriseId,
            OrderFilterRequestDTO filter,
            bool descending,
            int pageNumber,
            int pageSize);
        Task<Order?> GetByIdAsync(Guid id, Guid enterpriseId);
        Task<Order?> GetByExternalReferenceAsync(Guid enterpriseId, string externalReference);
        Task<string> GetOrdersBodyAsync(Enterprise? enterprise);
        Task InsertOrderInQueue(Order order);
        #endregion
    }
}
