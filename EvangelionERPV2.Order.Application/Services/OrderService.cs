using System.Linq;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    public class OrderService : IOrderService<Order>, IDisposable
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly IOrderRepository<Order> _orderRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct> _orderedProductRepository;
        private readonly IProductService<Product> _productService;
        public readonly IOrderRabbitMQManager _rabbitMQManager;
        public readonly IOrderReportGeneratorService _orderReportGeneratorService;
        private readonly IHubContext<OrderHub>? _orderHubContext;

        private bool disposed;

        public OrderService(EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            IOrderRepository<Order> orderRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct> orderedProductRepository,
            IProductService<Product> productService,
            IOrderRabbitMQManager rabbitMQManager,
            IOrderReportGeneratorService orderReportGeneratorService,
            IHubContext<OrderHub>? orderHubContext = null
            )
        {
            _orderRepository = orderRepository;
            _orderRepositoryCustom = orderRepositoryCustom;
            _productRepository = productRepository;
            _orderedProductRepository = orderedProductRepository;
            _productService = productService;
            _rabbitMQManager = rabbitMQManager;
            _orderReportGeneratorService = orderReportGeneratorService;
            _orderHubContext = orderHubContext;
        }

        #region Persistence

        public async Task<Order> CreateAsync(Order order)
        {
            try
            {
                if (order == null)
                    throw new InsertDatabaseException($"{nameof(Order)} is null");

                VerifyValidValues(ref order);

                await _orderRepository.CreateAsync(order);
                await _orderedProductRepository.CreateRangeAsync(order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>());

                // Update product quantity and needed fields for this flow
               await _productService.UpdateForOrder(order);

                await _orderRepository.CommitAsync();
                await _orderedProductRepository.CommitAsync();

                Log.Logger.Information($"Order [{order.Id}] created at: {DateTime.UtcNow}");

                // Send notification to Order Hub
                await SendOrderUpdate(order.Id.ToString(), "Created");

                return order;

            }
            catch (Exception ex)
            {
                await SendOrderUpdate(order.Id.ToString(), "Created");
                throw new InsertDatabaseException(ex.Message, ex);
            }
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

            if (order.PaymentScheduledDate.Date == DateTime.Now.Date)
                order.PaymentScheduledDate = order.PaymentScheduledDate.AddDays(30);

            if (order.TotalValue <= 0) throw new InsertDatabaseException($"{nameof(Order)} has value/quantity null or negative");
        }

        public Order Update(Order order)
        {
            try
            {
                Order existentOrder = _orderRepository.GetById(order.Id);
                Order updatedOrder = new Order();

                if (existentOrder == null)
                    throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                order.OrderedProduct = null;
                order.UpdatedAt = DateTime.UtcNow;
                updatedOrder = _orderRepository.Update(order);
                _orderRepository.Commit();
                return updatedOrder;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public Order Delete(Guid id)
        {
            try
            {
                Order order = _orderRepository.GetById(id);
                Order deletedOrder = new Order();

                if (order == null)
                    throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                order.IsActive = false;
                order.UpdatedAt = DateTime.UtcNow;
                deletedOrder = _orderRepository.Update(order);
                _orderRepository.Commit();
                return deletedOrder;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
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
                Log.Logger.Information($"Order was not able to be enqueued at: {DateTime.UtcNow}", ex);
                throw;
            }
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
                throw new Exception("The enterprise is null or empty");

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
