using System.Linq;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    public class OrderService : IOrderService<Order>, IDisposable
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly IOrderRepository<Order> _orderRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct> _orderedProductRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        private readonly IProductService<Product> _productService;
        public readonly IOrderRabbitMQManager _rabbitMQManager;
        public readonly IOrderReportGeneratorService _orderReportGeneratorService;
        private readonly IHubContext<OrderHub>? _orderHubContext;
        private readonly AppDbContext? _appDbContext;

        private bool disposed;

        public OrderService(EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            IOrderRepository<Order> orderRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct> orderedProductRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            IProductService<Product> productService,
            IOrderRabbitMQManager rabbitMQManager,
            IOrderReportGeneratorService orderReportGeneratorService,
            IHubContext<OrderHub>? orderHubContext = null,
            AppDbContext? appDbContext = null
            )
        {
            _orderRepository = orderRepository;
            _orderRepositoryCustom = orderRepositoryCustom;
            _productRepository = productRepository;
            _orderedProductRepository = orderedProductRepository;
            _enterpriseRepository = enterpriseRepository;
            _productService = productService;
            _rabbitMQManager = rabbitMQManager;
            _orderReportGeneratorService = orderReportGeneratorService;
            _orderHubContext = orderHubContext;
            _appDbContext = appDbContext;
        }

        #region Persistence

        public async Task<Order> CreateAsync(Order order)
        {
            try
            {
                if (order == null)
                    throw new InsertDatabaseException($"{nameof(Order)} is null");

                order.Id = Guid.NewGuid();
                order.Payday = ResolvePaydayForStatus(order.Payday, order.Status);

                var orderedProducts = order.OrderedProduct?.ToList() ?? [];
                foreach (var orderedProduct in orderedProducts)
                {
                    orderedProduct.Id = Guid.NewGuid();
                    orderedProduct.OrderId = order.Id;
                }
                order.OrderedProduct = orderedProducts;

                await ExecuteInTransactionAsync(async () =>
                {
                    VerifyValidValues(ref order);

                    await _orderRepository.CreateAsync(order);
                    await _orderedProductRepository.CreateRangeAsync(order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>());

                    // Update product quantity and needed fields for this flow
                    await _productService.UpdateForOrder(order);

                    await _orderRepository.CommitAsync();
                    await _orderedProductRepository.CommitAsync();

                    await ApplyEnterpriseBalanceDeltaAsync(
                        order.EnterpriseId ?? Guid.Empty,
                        EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(order, DateTime.UtcNow.Date));
                });

                Log.Logger.Information($"Order [{order.Id}] created at: {DateTime.UtcNow}");

                // Send notification to Order Hub
                await SendOrderUpdate(order.Id.ToString(), "Created");

                return order;

            }
            catch (Exception ex)
            {
                if (order != null && order.Id != Guid.Empty)
                    await SendOrderUpdate(order.Id.ToString(), "Failed");

                throw new InsertDatabaseException("Unexpected error while creating the order.", ex);
            }
        }

        public async Task<IEnumerable<Order>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null)
        {
            if (enterpriseId == Guid.Empty)
                return [];

            return await _orderRepository.GetAllAsync(
                pageNumber,
                pageSize,
                x => x.IsActive == true && x.EnterpriseId == enterpriseId);
        }

        public async Task<Order?> GetByIdAsync(Guid id, Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return null;

            var order = await _orderRepository.GetByIdAsync(id);
            if (order == null || order.IsActive != true || order.EnterpriseId != enterpriseId)
                return null;

            return order;
        }

        private async Task SendOrderUpdate(string orderId, string status)
        {
            try
            {
                if (_orderHubContext == null)
                    return;

                await _orderHubContext.Clients.All.SendAsync("ReceiveOrderUpdate", orderId, status);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "SignalR notification failed for Order {OrderId}", orderId);
            }
        }

        public void VerifyValidValues(ref Order order)
        {
            if (order == null) throw new InsertDatabaseException($"{nameof(Order)} is null");

            var orderedProducts = order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>();
            if (!orderedProducts.Any()) throw new InsertDatabaseException($"{nameof(Order)} has no products");

            if (orderedProducts.DistinctBy(x => x.ProductId).Count() != orderedProducts.Count()) throw new InsertDatabaseException($"{nameof(Order)} has duplicated items");

            if (orderedProducts.Any(x => x.Quantity <= 0 || x.Value <= 0)) throw new InsertDatabaseException($"{nameof(Order)} has products with quantity or value less than or equal to zero");

            if (orderedProducts.Any(x => x.Quantity == double.MaxValue || x.Value == double.MaxValue)) throw new InsertDatabaseException($"{nameof(Order)} has products with extremely large values");

            double computedTotal = orderedProducts
                .Where(x => x.Value > 0 && x.Quantity > 0)
                .Sum(x => x.Value * x.Quantity);

            order.TotalValue = computedTotal;

            if (order.PaymentScheduledDate.Date == DateTime.UtcNow.Date)
                order.PaymentScheduledDate = order.PaymentScheduledDate.AddDays(30);

            if (order.TotalValue <= 0) throw new InsertDatabaseException($"{nameof(Order)} has value/quantity null or negative");
        }

        public async Task<Order> RefundAsync(Guid id, Guid enterpriseId, string reason)
        {
            try
            {
                var trimmedReason = (reason ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmedReason))
                    throw new InsertDatabaseException("Refund reason is required.");

                Order refundedOrder = null!;

                await ExecuteInTransactionAsync(async () =>
                {
                    var existentOrder = await _orderRepository.GetByIdAsync(id);

                    if (existentOrder == null || existentOrder.EnterpriseId != enterpriseId || existentOrder.IsActive != true)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(existentOrder, DateTime.UtcNow.Date);

                    if (IsImmutableStatus(existentOrder.Status))
                        throw new InsertDatabaseException("Orders with status Finished or Refund cannot be edited.");

                    var orderedProducts = (await _orderedProductRepository.GetAllAsync(
                            x => x.IsActive == true && x.OrderId.HasValue && x.OrderId.Value == existentOrder.Id))
                        .ToList();

                    if (!orderedProducts.Any())
                        throw new InsertDatabaseException("Order has no items to refund.");

                    var productIds = orderedProducts.Select(x => x.ProductId).Distinct().ToList();
                    var productsById = (await _productRepository.GetAllAsync(
                            x => x.IsActive == true
                                 && x.EnterpriseId == enterpriseId
                                 && productIds.Contains(x.Id)))
                        .ToDictionary(x => x.Id, x => x);

                    if (productsById.Count != productIds.Count)
                        throw new InsertDatabaseException("Some products for this order were not found in the inventory.");

                    var now = DateTime.UtcNow;

                    foreach (var orderedProduct in orderedProducts)
                    {
                        if (!productsById.TryGetValue(orderedProduct.ProductId, out var product))
                            throw new InsertDatabaseException($"Product [{orderedProduct.ProductId}] was not found.");

                        var quantityToRestore = Math.Max(0, orderedProduct.Quantity);
                        product.StorageQuantity += quantityToRestore;
                        product.UpdatedAt = now;

                        orderedProduct.Quantity = 0;
                        orderedProduct.Value = 0;
                        orderedProduct.UpdatedAt = now;
                    }

                    _productRepository.UpdateRange(productsById.Values);
                    _orderedProductRepository.UpdateRange(orderedProducts);

                    existentOrder.TotalValue = 0;
                    existentOrder.Status = (int)EnumOrderStatus.Refund;
                    existentOrder.RefundReason = trimmedReason;
                    existentOrder.RefundedAt = now;
                    existentOrder.UpdatedAt = now;
                    _orderRepository.Update(existentOrder);

                    var newContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(existentOrder, now.Date);
                    await _orderRepository.CommitAsync();
                    await ApplyEnterpriseBalanceDeltaAsync(existentOrder.EnterpriseId ?? Guid.Empty, newContribution - oldContribution);
                    refundedOrder = existentOrder;
                });

                await SendOrderUpdate(refundedOrder.Id.ToString(), EnumOrderStatus.Refund.ToString());

                return refundedOrder;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while refunding the order.", ex);
            }
        }

        public Order Update(Order order)
        {
            return Update(order, order?.EnterpriseId ?? Guid.Empty);
        }

        public Order Update(Order order, Guid enterpriseId)
        {
            try
            {
                if (order == null)
                    throw new InsertDatabaseException($"{nameof(Order)} is null");

                return ExecuteInTransaction(() =>
                {
                    Order existentOrder = _orderRepository.GetById(order.Id);

                    if (existentOrder == null)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    if (enterpriseId != Guid.Empty && existentOrder.EnterpriseId != enterpriseId)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    if (order.EnterpriseId.HasValue
                        && order.EnterpriseId != Guid.Empty
                        && existentOrder.EnterpriseId != order.EnterpriseId)
                    {
                        throw new InsertDatabaseException("Order enterprise cannot be changed.");
                    }

                    if (IsImmutableStatus(existentOrder.Status))
                        throw new InsertDatabaseException("Orders with status Finished or Refund cannot be edited.");

                    if (order.Status == (int)EnumOrderStatus.Refund)
                        throw new InsertDatabaseException("Use refund action to set order as Refund.");

                    if (!Enum.IsDefined(typeof(EnumOrderStatus), order.Status))
                        throw new InsertDatabaseException("Invalid order status.");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(existentOrder, DateTime.UtcNow.Date);

                    // Use tracked merge to avoid detached-entity overwrite and preserve immutable fields.
                    existentOrder.CustomerId = order.CustomerId ?? existentOrder.CustomerId;
                    existentOrder.UserId = order.UserId ?? existentOrder.UserId;
                    existentOrder.Payday = ResolvePaydayForStatus(order.Payday, order.Status);
                    if (order.PaymentScheduledDate != default)
                        existentOrder.PaymentScheduledDate = order.PaymentScheduledDate;

                    if (order.TotalValue > 0)
                        existentOrder.TotalValue = order.TotalValue;

                    existentOrder.Status = order.Status;
                    existentOrder.OrderedProduct = null;
                    existentOrder.UpdatedAt = DateTime.UtcNow;

                    var updatedOrder = _orderRepository.Update(existentOrder);

                    var newContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(updatedOrder, DateTime.UtcNow.Date);
                    _orderRepository.Commit();
                    ApplyEnterpriseBalanceDelta(updatedOrder.EnterpriseId ?? Guid.Empty, newContribution - oldContribution);

                    return updatedOrder;
                });
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating the order.", ex);
            }
        }

        public Order Delete(Guid id)
        {
            return Delete(id, Guid.Empty);
        }

        public Order Delete(Guid id, Guid enterpriseId)
        {
            try
            {
                return ExecuteInTransaction(() =>
                {
                    Order order = _orderRepository.GetById(id);
                    Order deletedOrder = new Order();

                    if (order == null)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    if (enterpriseId != Guid.Empty && order.EnterpriseId != enterpriseId)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    if (IsImmutableStatus(order.Status))
                        throw new InsertDatabaseException("Orders with status Finished or Refund cannot be edited.");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(order, DateTime.UtcNow.Date);

                    order.IsActive = false;
                    order.UpdatedAt = DateTime.UtcNow;
                    deletedOrder = _orderRepository.Update(order);
                    _orderRepository.Commit();
                    ApplyEnterpriseBalanceDelta(order.EnterpriseId ?? Guid.Empty, -oldContribution);

                    return deletedOrder;
                });
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while deleting the order.", ex);
            }
        }

        public async Task InsertOrderInQueue(Order order)
        {
            try
            {
                Log.Logger.Information($"Order enqueing at: {DateTime.UtcNow}");
                await _rabbitMQManager.EnqueueAsync(order);
                Log.Logger.Information($"Order enqueued at: {DateTime.UtcNow}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, $"Order was not able to be enqueued at: {DateTime.UtcNow}");
                throw;
            }
        }

        private static bool IsImmutableStatus(int status)
        {
            return status == (int)EnumOrderStatus.Finished || status == (int)EnumOrderStatus.Refund;
        }

        private static DateTime? ResolvePaydayForStatus(DateTime? payday, int status)
        {
            if (status == (int)EnumOrderStatus.Finished && !payday.HasValue)
                return DateTime.UtcNow;

            return payday;
        }

        private async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            await _orderRepository.ExecuteInTransactionAsync(operation);
        }

        private T ExecuteInTransaction<T>(Func<T> operation)
        {
            return _orderRepository.ExecuteInTransaction(operation);
        }

        private void ApplyEnterpriseBalanceDelta(Guid enterpriseId, double delta)
        {
            if (enterpriseId == Guid.Empty || Math.Abs(delta) < 0.000001)
                return;

            if (_appDbContext != null)
            {
                var now = DateTime.UtcNow;
                var roundedDelta = Math.Round(delta, 2);
                var affectedRows = _appDbContext.Database.ExecuteSqlInterpolated(
                    $"UPDATE Enterprise SET CurrentBalance = ROUND(CurrentBalance + {roundedDelta}, 2), UpdatedAt = {now} WHERE Id = {enterpriseId}");

                if (affectedRows <= 0)
                    throw new InsertDatabaseException("Enterprise was not found for balance update.");

                return;
            }

            var enterprise = _enterpriseRepository.GetById(enterpriseId);
            if (enterprise == null)
                throw new InsertDatabaseException("Enterprise was not found for balance update.");

            enterprise.CurrentBalance = Math.Round(enterprise.CurrentBalance + delta, 2);
            enterprise.UpdatedAt = DateTime.UtcNow;
            _enterpriseRepository.Update(enterprise);
            _enterpriseRepository.Commit();
        }

        private async Task ApplyEnterpriseBalanceDeltaAsync(Guid enterpriseId, double delta)
        {
            if (enterpriseId == Guid.Empty || Math.Abs(delta) < 0.000001)
                return;

            if (_appDbContext != null)
            {
                var now = DateTime.UtcNow;
                var roundedDelta = Math.Round(delta, 2);
                var affectedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Enterprise SET CurrentBalance = ROUND(CurrentBalance + {roundedDelta}, 2), UpdatedAt = {now} WHERE Id = {enterpriseId}");

                if (affectedRows <= 0)
                    throw new InsertDatabaseException("Enterprise was not found for balance update.");

                return;
            }

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            if (enterprise == null)
                throw new InsertDatabaseException("Enterprise was not found for balance update.");

            enterprise.CurrentBalance = Math.Round(enterprise.CurrentBalance + delta, 2);
            enterprise.UpdatedAt = DateTime.UtcNow;
            _enterpriseRepository.Update(enterprise);
            await _enterpriseRepository.CommitAsync();
        }

        #endregion

        #region Billing
        /// <summary>
        /// Get orders body and apply rules
        /// </summary>
        public async Task<string> GetOrdersBodyAsync(Enterprise? enterprise)
        {
            if (!DateTime.UtcNow.IsLastMonthDay())
                return string.Empty;

            if (enterprise == null)
                throw new ArgumentNullException(nameof(enterprise), "The enterprise is null or empty");

            IEnumerable<Order>? orders = await _orderRepositoryCustom.GetAllAsyncWithOrderedProductsByEnterprise(enterprise);

            if (orders == null || !orders.Any())
            {
                Log.Logger.Warning($"Doesn't have any orders to be billed for {enterprise.Name}");
                return string.Empty;
            }

            var ordersList = orders.ToList();
            return await _orderReportGeneratorService.GenerateMonthlyBillingReportAsync(enterprise, ordersList);
        }

        #endregion

        #region Dispose Pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here.
                    (_orderRepository as IDisposable)?.Dispose();
                    (_orderRepositoryCustom as IDisposable)?.Dispose();
                    (_productRepository as IDisposable)?.Dispose();
                    (_orderedProductRepository as IDisposable)?.Dispose();
                    (_enterpriseRepository as IDisposable)?.Dispose();
                    (_productService as IDisposable)?.Dispose();
                    (_rabbitMQManager as IDisposable)?.Dispose();
                    (_orderReportGeneratorService as IDisposable)?.
Dispose();
                    (_orderHubContext as IDisposable)?.Dispose();
                }

                // Dispose unmanaged resources here.
                // For example:
                // Close file handles, release COM objects.

                disposed = true;
            }
        }

        // Destructor for finalization code
        ~OrderService()
        {
            Dispose(false);
        }

        #endregion
    }
}
