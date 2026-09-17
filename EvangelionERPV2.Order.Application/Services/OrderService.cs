using EvangelionERPV2.Shared.DTOs;
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
using Microsoft.Data.SqlClient;
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
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Bill> _billRepository;
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
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Bill> billRepository,
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
            _customerRepository = customerRepository;
            _billRepository = billRepository;
            _productService = productService;
            _rabbitMQManager = rabbitMQManager;
            _orderReportGeneratorService = orderReportGeneratorService;
            _orderHubContext = orderHubContext;
            _appDbContext = appDbContext;
        }

        #region Persistence

        /// <param name="allowCreditOverride">
        /// Skips the customer's credit limit. Defaulted so no existing caller changes and the
        /// queued path — which has no user behind it — always enforces; the controller sets it only
        /// for a caller holding customers.credit.override.
        /// </param>
        /// <param name="allowNegativeStock">
        /// Threaded to the stock path. Only the PDV offline sync sets it, and only because that
        /// sale is already history: the customer has the goods and refusing the movement would
        /// lose the revenue without restoring the stock. Defaulted so no existing caller changes,
        /// exactly like <paramref name="allowCreditOverride"/>.
        /// </param>
        public async Task<Order> CreateAsync(Order order, bool allowCreditOverride = false, bool allowNegativeStock = false, string? messageId = null)
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
                    // Claimed inside the transaction, before any work: if this message has already
                    // been handled the unique index rejects the row and the whole order rolls back,
                    // so the duplicate never reaches stock or the enterprise balance.
                    await ClaimMessageAsync(messageId);

                    VerifyValidValues(ref order);
                    await AssertPeriodOpenAsync(order.EnterpriseId ?? Guid.Empty, order.PaymentScheduledDate, "This order");
                    await AssertWithinCreditLimitAsync(order, allowCreditOverride);
                    await ValidateOrderProductsAsync(order);

                    await _orderRepository.CreateAsync(order);
                    await _orderedProductRepository.CreateRangeAsync(order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>());

                    // Update product quantity and needed fields for this flow
                    await _productService.UpdateForOrder(order, allowNegativeStock);

                    await _orderRepository.CommitAsync();
                    await _orderedProductRepository.CommitAsync();

                    await ApplyEnterpriseBalanceDeltaAsync(
                        order.EnterpriseId ?? Guid.Empty,
                        EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(order, DateTime.UtcNow.Date));
                });
            }
            catch (PeriodClosedException)
            {
                throw;
            }
            catch (CreditLimitExceededException)
            {
                throw;
            }
            // Must pass through, like the two above. Wrapped in InsertDatabaseException it reaches
            // the controller as a 400, the worker reads that as a failure and retries, and the
            // message it already handled ends up dead-lettered - which is what happened the first
            // time this was tested.
            catch (DuplicateMessageException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                if (order != null && order.Id != Guid.Empty)
                    await SendOrderUpdate(order.Id.ToString(), "Failed", order.EnterpriseId ?? Guid.Empty);

                throw new InsertDatabaseException("Unexpected error while creating the order.", ex);
            }
            catch (Exception ex)
            {
                if (order != null && order.Id != Guid.Empty)
                    await SendOrderUpdate(order.Id.ToString(), "Failed", order.EnterpriseId ?? Guid.Empty);

                throw new InsertDatabaseException("Unexpected error while creating the order.", ex);
            }

            Log.Logger.Information($"Order [{order.Id}] created at: {DateTime.UtcNow}");

            // Send notification to Order Hub
            await SendOrderUpdate(order.Id.ToString(), "Created", order.EnterpriseId ?? Guid.Empty);

            return order;
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

        private async Task SendOrderUpdate(string orderId, string status, Guid enterpriseId)
        {
            try
            {
                if (_orderHubContext == null)
                    return;

                if (enterpriseId == Guid.Empty)
                    return;

                await _orderHubContext.Clients
                    .Group(enterpriseId.ToString())
                    .SendAsync("ReceiveOrderUpdate", orderId, status);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("SignalR notification failed for Order {OrderId}. ErrorType={ErrorType}", orderId, ex.GetType().Name);
            }
        }

        public void VerifyValidValues(ref Order order)
        {
            if (order == null) throw new InsertDatabaseException($"{nameof(Order)} is null");

            var orderedProducts = order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>();
            if (!orderedProducts.Any()) throw new InsertDatabaseException($"{nameof(Order)} has no products");

            if (orderedProducts.DistinctBy(x => x.ProductId).Count() != orderedProducts.Count()) throw new InsertDatabaseException($"{nameof(Order)} has duplicated items");

            if (orderedProducts.Any(x => x.Quantity <= 0 || x.Value <= 0)) throw new InsertDatabaseException($"{nameof(Order)} has products with quantity or value less than or equal to zero");

            if (orderedProducts.Any(x => x.Quantity == decimal.MaxValue || x.Value == decimal.MaxValue)) throw new InsertDatabaseException($"{nameof(Order)} has products with extremely large values");

            decimal computedTotal = orderedProducts
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
                    var now = DateTime.UtcNow;
                    var existentOrder = await _orderRepository.GetByIdAsync(id);

                    if (existentOrder == null || existentOrder.EnterpriseId != enterpriseId || existentOrder.IsActive != true)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(existentOrder, DateTime.UtcNow.Date);

                    // Deliberately not IsImmutableStatus. Finished means the order can no longer be
                    // rewritten, not that money can never be given back — a finished sale is
                    // precisely what a refund exists to reverse, and every till sale is Finished on
                    // arrival. Already-refunded is the only status a refund refuses; the period
                    // lock below is what actually protects the books.
                    if (existentOrder.Status == (int)EnumOrderStatus.Refund || existentOrder.RefundedAt.HasValue)
                        throw new InsertDatabaseException("Order was already refunded.");

                    // The order's own date, not today: a refund reverses revenue in the month the
                    // order was booked into, which is the month that must still be open.
                    await AssertPeriodOpenAsync(enterpriseId, existentOrder.PaymentScheduledDate, "The order being refunded");

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

                    if (_appDbContext != null)
                    {
                        var affectedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE [Order] SET TotalValue = 0, Status = {(int)EnumOrderStatus.Refund}, RefundReason = {trimmedReason}, RefundedAt = {now}, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND Status <> {(int)EnumOrderStatus.Refund}");

                        if (affectedRows <= 0)
                            throw new InsertDatabaseException("Order was already refunded.");
                    }

                    foreach (var orderedProduct in orderedProducts)
                    {
                        if (!productsById.TryGetValue(orderedProduct.ProductId, out var product))
                            throw new InsertDatabaseException($"Product [{orderedProduct.ProductId}] was not found.");

                        var quantityToRestore = Math.Max(0m, orderedProduct.Quantity);

                        // Goes through the shared stock path rather than mutating the entity here:
                        // a refund that puts units back is a stock movement like any other and has
                        // to leave a ledger row and drop the product cache, same as the sale did.
                        // A zero-quantity line moved nothing, and the ledger only records changes.
                        if (quantityToRestore > 0)
                        {
                            await _productService.ApplyStockMovementAsync(
                                product.Id,
                                enterpriseId,
                                quantityToRestore,
                                EnumStockMovementType.Refund,
                                existentOrder.Id);
                        }

                        orderedProduct.Quantity = 0;
                        orderedProduct.Value = 0;
                        orderedProduct.UpdatedAt = now;
                    }

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

                await SendOrderUpdate(
                    refundedOrder.Id.ToString(),
                    EnumOrderStatus.Refund.ToString(),
                    refundedOrder.EnterpriseId ?? Guid.Empty);

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
            catch (PeriodClosedException)
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

                    // Both dates: the month it sits in has to be open, and so does the month it is
                    // being moved to — otherwise an edit could carry a document into closed books.
                    var lockedThrough = GetClosedThroughUtc(existentOrder.EnterpriseId ?? enterpriseId);
                    PeriodLock.Assert(lockedThrough, existentOrder.PaymentScheduledDate, "This order");
                    if (order.PaymentScheduledDate != default)
                        PeriodLock.Assert(lockedThrough, order.PaymentScheduledDate, "The new order date");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(existentOrder, DateTime.UtcNow.Date);
                    var now = DateTime.UtcNow;

                    if (_appDbContext != null)
                    {
                        var guardedRows = _appDbContext.Database.ExecuteSqlInterpolated(
                            $"UPDATE [Order] SET UpdatedAt = {now} WHERE Id = {order.Id} AND EnterpriseId = {existentOrder.EnterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND Status <> {(int)EnumOrderStatus.Refund} AND Status <> {(int)EnumOrderStatus.Finished}");

                        if (guardedRows <= 0)
                            throw new InsertDatabaseException("Order was concurrently finalized or already refunded.");
                    }

                    // Use tracked merge to avoid detached-entity overwrite and preserve immutable fields.
                    existentOrder.CustomerId = order.CustomerId ?? existentOrder.CustomerId;
                    existentOrder.UserId = order.UserId ?? existentOrder.UserId;
                    existentOrder.Payday = ResolvePaydayForStatus(
                        existentOrder.Payday,
                        order.Payday,
                        existentOrder.Status,
                        order.Status,
                        now);
                    if (order.PaymentScheduledDate != default)
                        existentOrder.PaymentScheduledDate = order.PaymentScheduledDate;

                    existentOrder.Status = order.Status;
                    existentOrder.OrderedProduct = null;
                    existentOrder.UpdatedAt = now;

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
            catch (PeriodClosedException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                // A business rule saying no — "this order is Finished", "stock is short". Rewrapping
                // it hid the reason behind an inner exception and the controller answered 500 to
                // what is a 400 with a sentence the caller can act on.
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
                    var now = DateTime.UtcNow;
                    Order order = _orderRepository.GetById(id);
                    Order deletedOrder = new Order();

                    if (order == null)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    if (enterpriseId != Guid.Empty && order.EnterpriseId != enterpriseId)
                        throw new NotFoundDatabaseException($"{nameof(Order)} was not found in database.");

                    if (IsImmutableStatus(order.Status))
                        throw new InsertDatabaseException("Orders with status Finished or Refund cannot be edited.");

                    // Deleting is how revenue leaves a month, so it is governed exactly like an edit.
                    PeriodLock.Assert(
                        GetClosedThroughUtc(order.EnterpriseId ?? enterpriseId),
                        order.PaymentScheduledDate,
                        "This order");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(order, DateTime.UtcNow.Date);

                    if (_appDbContext != null)
                    {
                        var affectedRows = _appDbContext.Database.ExecuteSqlInterpolated(
                            $"UPDATE [Order] SET IsActive = 0, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND Status <> {(int)EnumOrderStatus.Refund} AND Status <> {(int)EnumOrderStatus.Finished}");

                        if (affectedRows <= 0)
                            throw new InsertDatabaseException("Order was concurrently finalized or already refunded.");
                    }

                    order.IsActive = false;
                    order.UpdatedAt = now;
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
            catch (PeriodClosedException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                // A business rule saying no — "this order is Finished", "stock is short". Rewrapping
                // it hid the reason behind an inner exception and the controller answered 500 to
                // what is a 400 with a sentence the caller can act on.
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while deleting the order.", ex);
            }
        }

        /// <summary>
        /// Publishes the order as a flat message contract rather than as the entity.
        /// </summary>
        /// <remarks>
        /// The entity was serialized with <c>ReferenceHandler.Preserve</c> because its navigation
        /// properties cycle, which put <c>$id</c>/<c>$ref</c> on the wire and made the queue format
        /// an accident of the EF model: any new navigation property changed what consumers received.
        /// <see cref="CreateQueuedOrderRequestDTO"/> is what the worker has always reduced the
        /// message to, so nothing is lost by sending it directly.
        /// </remarks>
        public async Task InsertOrderInQueue(Order order)
        {
            try
            {
                Log.Logger.Information($"Order enqueing at: {DateTime.UtcNow}");
                await _rabbitMQManager.EnqueueAsync(ToQueuedMessage(order));
                Log.Logger.Information($"Order enqueued at: {DateTime.UtcNow}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Order was not able to be enqueued at: {OccurredAt}. ErrorType={ErrorType}", DateTime.UtcNow, ex.GetType().Name);
                throw;
            }
        }

        private static CreateQueuedOrderRequestDTO ToQueuedMessage(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            return new CreateQueuedOrderRequestDTO
            {
                EnterpriseId = order.EnterpriseId ?? Guid.Empty,
                UserId = order.UserId ?? Guid.Empty,
                CustomerId = order.CustomerId ?? Guid.Empty,
                PaymentScheduledDate = order.PaymentScheduledDate,
                Status = order.Status,
                Items = (order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
                    .Select(item => new OrderLineItemRequestDTO
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        Value = item.Value
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Records that a queue message has been handled, or throws
        /// <see cref="DuplicateMessageException"/> if it already was.
        /// </summary>
        /// <remarks>
        /// The unique index on (Handler, MessageId) is what settles the race between two concurrent
        /// deliveries: both read an empty table, one commits, the other violates the constraint.
        /// A check without the constraint would be a race, and a constraint without the transaction
        /// would leave the row behind when the order failed for some other reason.
        ///
        /// A null or empty id means the caller is not a queue consumer - the direct API path - and
        /// there is nothing to deduplicate.
        /// </remarks>
        private async Task ClaimMessageAsync(string? messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId) || _appDbContext == null)
                return;

            if (await _appDbContext.ProcessedMessage
                    .AsNoTracking()
                    .AnyAsync(processed => processed.Handler == OrderMessageHandler && processed.MessageId == messageId))
            {
                throw new DuplicateMessageException(messageId);
            }

            _appDbContext.ProcessedMessage.Add(new ProcessedMessage
            {
                Id = Guid.NewGuid(),
                Handler = OrderMessageHandler,
                MessageId = messageId,
                ProcessedAt = DateTime.UtcNow
            });

            try
            {
                await _appDbContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // The other delivery won. Same outcome as the check above, reached the only way that
                // is safe when two workers are processing the same message at once.
                throw new DuplicateMessageException(messageId);
            }
        }

        private static bool IsUniqueViolation(DbUpdateException exception)
        {
            // 2601 duplicate key in a unique index, 2627 unique constraint violation.
            return exception.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
        }

        private const string OrderMessageHandler = "order";

        private static bool IsImmutableStatus(int status)
        {
            return status == (int)EnumOrderStatus.Finished || status == (int)EnumOrderStatus.Refund;
        }

        private static DateTime? ResolvePaydayForStatus(DateTime? payday, int status)
        {
            if (IsPaydayStatus(status) && !payday.HasValue)
                return DateTime.UtcNow;

            return IsPaydayStatus(status) ? payday : null;
        }

        private static DateTime? ResolvePaydayForStatus(
            DateTime? currentPayday,
            DateTime? requestedPayday,
            int currentStatus,
            int requestedStatus,
            DateTime transitionDate)
        {
            if (!IsPaydayStatus(requestedStatus))
                return null;

            if (currentStatus != requestedStatus)
                return transitionDate;

            return requestedPayday ?? currentPayday ?? transitionDate;
        }

        private static bool IsPaydayStatus(int status)
        {
            return status == (int)EnumOrderStatus.Paid
                || status == (int)EnumOrderStatus.Shipped
                || status == (int)EnumOrderStatus.Delivered
                || status == (int)EnumOrderStatus.Finished;
        }

        private async Task ValidateOrderProductsAsync(Order order)
        {
            var orderEnterpriseId = order.EnterpriseId ?? Guid.Empty;
            if (orderEnterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Order enterprise is required.");

            var requestedProductIds = (order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
                .Select(x => x.ProductId)
                .Distinct()
                .ToList();

            if (!requestedProductIds.Any())
                throw new InsertDatabaseException("Order must contain at least one product.");

            var products = await _productRepository.GetAllAsync(x =>
                x.IsActive == true &&
                x.EnterpriseId == orderEnterpriseId &&
                requestedProductIds.Contains(x.Id));

            if (products.Count() != requestedProductIds.Count)
                throw new InsertDatabaseException("Some products were not found in enterprise inventory.");

            SnapshotLineCosts(order, products);
        }

        /// <summary>
        /// Copies each product's CostPrice onto its order line. A snapshot, not a join: the margin
        /// of an order booked today must not change the next time somebody edits a product's cost.
        /// A product with no cost recorded leaves the line null, and OrderMargin then reports the
        /// whole order's margin as unknown rather than inventing one.
        /// </summary>
        private static void SnapshotLineCosts(Order order, IEnumerable<Product> products)
        {
            var costByProductId = products.ToDictionary(x => x.Id, x => x.CostPrice);

            foreach (var line in order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
            {
                if (costByProductId.TryGetValue(line.ProductId, out var cost))
                    line.UnitCost = cost;
            }
        }

        private async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            await _orderRepository.ExecuteInTransactionAsync(operation);
        }

        private T ExecuteInTransaction<T>(Func<T> operation)
        {
            return _orderRepository.ExecuteInTransaction(operation);
        }

        private void ApplyEnterpriseBalanceDelta(Guid enterpriseId, decimal delta)
        {
            if (enterpriseId == Guid.Empty || Math.Abs(delta) < 0.000001m)
                return;

            if (_appDbContext != null)
            {
                var now = DateTime.UtcNow;
                // BRL convention is half-away-from-zero, and SQL Server's ROUND() below rounds
                // that way too — Math.Round defaults to banker's rounding and would disagree.
                var roundedDelta = Math.Round(delta, 2, MidpointRounding.AwayFromZero);
                var affectedRows = _appDbContext.Database.ExecuteSqlInterpolated(
                    $"UPDATE Enterprise SET CurrentBalance = ROUND(CurrentBalance + {roundedDelta}, 2), UpdatedAt = {now} WHERE Id = {enterpriseId}");

                if (affectedRows <= 0)
                    throw new InsertDatabaseException("Enterprise was not found for balance update.");

                return;
            }

            var enterprise = _enterpriseRepository.GetById(enterpriseId);
            if (enterprise == null)
                throw new InsertDatabaseException("Enterprise was not found for balance update.");

            enterprise.CurrentBalance = Math.Round(enterprise.CurrentBalance + delta, 2, MidpointRounding.AwayFromZero);
            enterprise.UpdatedAt = DateTime.UtcNow;
            _enterpriseRepository.Update(enterprise);
            _enterpriseRepository.Commit();
        }

        /// <summary>The tenant's period lock, or null when nothing is closed.</summary>
        private DateTime? GetClosedThroughUtc(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return null;

            return _enterpriseRepository.GetById(enterpriseId)?.ClosedThroughUtc;
        }

        /// <summary>
        /// Refuses an order that would put the customer past the limit set for them.
        /// </summary>
        /// <remarks>
        /// Exposure is computed here rather than stored: a running total on Customer drifts the
        /// moment a boleto is settled outside the app. The rule for what an order still owes is
        /// <see cref="ReceivableResolver.ResolveOpenExposure"/> — the same one the aging report,
        /// the panel and the forecast use, so the number a salesperson is blocked by is the number
        /// the carteira shows.
        ///
        /// Inside the creation transaction, and the customer's own row is locked before the
        /// carteira is read, so two simultaneous orders for the same customer queue behind each
        /// other: the second reads the carteira with the first order already in it instead of
        /// both reading the total from before either. Only that customer's row is held — orders
        /// for anyone else never wait on it.
        /// </remarks>
        private async Task AssertWithinCreditLimitAsync(Order order, bool allowCreditOverride)
        {
            if (allowCreditOverride)
                return;

            var enterpriseId = order.EnterpriseId ?? Guid.Empty;
            var customerId = order.CustomerId ?? Guid.Empty;
            if (enterpriseId == Guid.Empty || customerId == Guid.Empty)
                return;

            // GetPagedAsync, not GetAllAsync: CustomerRepository's override throws on an empty
            // result, and a missing customer must leave the order to the foreign key to refuse —
            // not turn into a generic "unexpected error while creating the order".
            var customer = (await _customerRepository.GetPagedAsync(
                    x => x.Id == customerId && x.EnterpriseId == enterpriseId))
                .Items
                .FirstOrDefault();

            var creditLimit = customer?.CreditLimit;
            if (creditLimit == null)
                return;

            // The gate between the read below and the insert this transaction is about to make.
            // Without it two orders for this customer both read the carteira before either is in
            // it, both fit, and the pair goes over. Taken only once a limit is actually set, so a
            // customer with no limit still contends with nobody.
            await _customerRepository.LockForUpdateAsync(customerId);

            var exposure = await GetOpenExposureAsync(enterpriseId, customerId);
            var projected = exposure + order.TotalValue;
            if (projected <= creditLimit.Value)
                return;

            // Amounts only. The message reaches the caller through the 409, and it is the
            // company's own figures — never anything the request submitted.
            throw new CreditLimitExceededException(
                $"This order would take the customer to {projected:0.00} against a credit limit of {creditLimit.Value:0.00}.");
        }

        /// <summary>Everything this customer still owes, boleto first, refunds excluded.</summary>
        private async Task<decimal> GetOpenExposureAsync(Guid enterpriseId, Guid customerId)
        {
            // GetPagedAsync deliberately: OrderRepository's GetAllAsync override drags the whole
            // item graph across the wire, and this reads four columns.
            var openOrders = (await _orderRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId
                         && x.CustomerId == customerId
                         && x.IsActive == true
                         && x.TotalValue > 0
                         && x.RefundedAt == null
                         && x.Status != (int)EnumOrderStatus.Refund
                         && x.Status != (int)EnumOrderStatus.Paid
                         && x.Status != (int)EnumOrderStatus.Finished))
                .Items;

            if (openOrders.Count == 0)
                return 0m;

            // Bill carries no EnterpriseId of its own; it is tenant-scoped through Order.
            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(
                await _billRepository.GetAllAsync(
                    x => x.Order != null
                         && x.Order.EnterpriseId == enterpriseId
                         && x.Order.CustomerId == customerId
                         && (x.IsActive == null || x.IsActive == true)));

            return openOrders.Sum(x => ReceivableResolver.ResolveOpenExposure(x, liveBills.GetValueOrDefault(x.Id)));
        }

        private async Task AssertPeriodOpenAsync(Guid enterpriseId, DateTime businessDate, string what)
        {
            if (enterpriseId == Guid.Empty)
                return;

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            PeriodLock.Assert(enterprise?.ClosedThroughUtc, businessDate, what);
        }

        private async Task ApplyEnterpriseBalanceDeltaAsync(Guid enterpriseId, decimal delta)
        {
            if (enterpriseId == Guid.Empty || Math.Abs(delta) < 0.000001m)
                return;

            if (_appDbContext != null)
            {
                var now = DateTime.UtcNow;
                // BRL convention is half-away-from-zero, and SQL Server's ROUND() below rounds
                // that way too — Math.Round defaults to banker's rounding and would disagree.
                var roundedDelta = Math.Round(delta, 2, MidpointRounding.AwayFromZero);
                var affectedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Enterprise SET CurrentBalance = ROUND(CurrentBalance + {roundedDelta}, 2), UpdatedAt = {now} WHERE Id = {enterpriseId}");

                if (affectedRows <= 0)
                    throw new InsertDatabaseException("Enterprise was not found for balance update.");

                return;
            }

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            if (enterprise == null)
                throw new InsertDatabaseException("Enterprise was not found for balance update.");

            enterprise.CurrentBalance = Math.Round(enterprise.CurrentBalance + delta, 2, MidpointRounding.AwayFromZero);
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
