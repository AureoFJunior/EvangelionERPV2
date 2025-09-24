
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Serilog;
using System.Text;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    public class OrderService : IOrderService<Order>, IDisposable
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly IOrderRepository<Order> _orderRepositoryCustom;
        private readonly IRepository<Product> _productRepository;
        private readonly IRepository<OrderedProduct> _orderedProductRepository;
        private readonly IProductService<Product> _productService;
        public readonly IOrderRabbitMQManager _rabbitMQManager;

        private bool disposed;

        public OrderService(IRepository<Order> orderRepository,
            IOrderRepository<Order> orderRepositoryCustom,
            IRepository<Product> productRepository,
            IRepository<OrderedProduct> orderedProductRepository,
            IProductService<Product> productService,
            IOrderRabbitMQManager rabbitMQManager
            )
        {
            _orderRepository = orderRepository;
            _orderRepositoryCustom = orderRepositoryCustom;
            _productRepository = productRepository;
            _orderedProductRepository = orderedProductRepository;
            _productService = productService;
            _rabbitMQManager = rabbitMQManager;
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
                await _orderedProductRepository.CreateRangeAsync(order.OrderedProduct);

                // Update product quantity and needed fields for this flow
                await _productService.UpdateForOrder(order);

                await _orderRepository.CommitAsync();
                await _orderedProductRepository.CommitAsync();

                Log.Logger.Information($"Order [{order.Id}] created at: {DateTime.UtcNow}");
                return order;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public void VerifyValidValues(ref Order order)
        {
            order.TotalValue = order.OrderedProduct.Where(x => x.Value > 0 && x.Quantity > 0).Select(x => x.Value * x.Quantity).FirstOrDefault();

            if (order.TotalValue == null || order.TotalValue <= 0) { throw new InsertDatabaseException($"{nameof(Order)} has value/quantity null or negative"); }
            if (order.OrderedProduct?.DistinctBy(x => x.ProductId).Count() != order.OrderedProduct?.Count()) { throw new InsertDatabaseException($"{nameof(Order)} has duplicated items"); }
            if (order.OrderedProduct == null || !order.OrderedProduct.Any()) { throw new InsertDatabaseException($"{nameof(Order)} has no products"); }
            if (order.OrderedProduct?.Any(x => x.Quantity <= 0 || x.Value <= 0) ?? false) { throw new InsertDatabaseException($"{nameof(Order)} has products with quantity or value less than or equal to zero"); }
            if (order.OrderedProduct?.Any(x => x.Quantity == double.MaxValue || x.Value == double.MaxValue) ?? false) { throw new InsertDatabaseException($"{nameof(Order)} has products with extremely large values"); }
        }

        public Order Update(Order order)
        {
            try
            {
                Order existentOrder = _orderRepository.GetById(order.Id);
                Order updatedOrder = new Order();

                if (existentOrder == null)
                    throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                order.UpdatedAt = DateTime.UtcNow;
                updatedOrder = _orderRepository.Update(order);
                _orderRepository.Commit();
                return updatedOrder;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
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
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
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
            //if (!DateTime.UtcNow.IsLastMonthDay())
                //return null;

            if (enterprise == null)
                throw new Exception("The enterprise is null or empty");

            IEnumerable<Order> orders = await _orderRepositoryCustom.GetAllAsyncWithOrderedProductsByEnterprise(enterprise);

            if (orders == null || orders?.Any() == false)
            {
                Log.Logger.Warning($"Doesn't have any orders to be billed for {enterprise.Name}");
                return null;
            }

            return await BuildOrdersBody(orders, enterprise);
        }

        private async Task<string> BuildOrdersBody(IEnumerable<Order> orders, Enterprise enterprise)
        {
            var body = new StringBuilder();

            // Add header
            body.AppendLine("<h2>Monthly Billing</h2>");
            body.AppendLine($"<h3>{enterprise.Name}</h3>");
            body.AppendLine("<table border='1'>");
            body.AppendLine("<thead>");
            body.AppendLine("<tr>");
            body.AppendLine("<th>Product</th>");
            body.AppendLine("<th>Quantity</th>");
            body.AppendLine("<th>Value</th>");
            body.AppendLine("</tr>");
            body.AppendLine("</thead>");
            body.AppendLine("<tbody>");

            // Add order details
            double totalQuantity = 0;
            double totalValue = 0;
            foreach (var order in orders)
            {
                foreach (var orderedProduct in order.OrderedProduct)
                {
                    var product = await _productRepository.GetByIdAsync(orderedProduct.Id) ?? new Product();

                    // Build product info
                    body.AppendLine("<tr>");
                    body.AppendLine($"<td>{product?.Name}</td>");
                    body.AppendLine($"<td>{orderedProduct.Quantity}</td>");
                    body.AppendLine($"<td>{orderedProduct.Value:C}</td>");
                    body.AppendLine("</tr>");
                }
                totalQuantity += order.OrderedProduct.Sum(x => x.Quantity);
                totalValue += order.TotalValue;
            }

            // Add totals
            body.AppendLine("<tr>");
            body.AppendLine($"<td><b>Total</b></td>");
            body.AppendLine($"<td><b>Itens Count: {totalQuantity}</b></td>");
            body.AppendLine($"<td><b>Total Value: {totalValue:C}</b></td>");
            body.AppendLine("</tr>");

            body.AppendLine("</tbody>");
            body.AppendLine("</table>");

            return body.ToString();

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
                    (_productRepository as IDisposable)?.Dispose();
                    (_productService as IDisposable)?.Dispose();
                    (_rabbitMQManager as IDisposable)?.Dispose();
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