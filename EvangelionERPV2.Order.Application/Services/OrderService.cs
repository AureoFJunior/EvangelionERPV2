using EvangelionERPV2.Shared.DTOs;
using System.Linq;
using System.Linq.Expressions;
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
    public class OrderService : IOrderService<Order>
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
                    await ClaimMessageAsync(messageId);

                    VerifyValidValues(ref order);
                    await AssertPeriodOpenAsync(order.EnterpriseId ?? Guid.Empty, order.PaymentScheduledDate, "This order");
                    await AssertWithinCreditLimitAsync(order, allowCreditOverride);
                    await ValidateOrderProductsAsync(order);

                    await _orderRepository.CreateAsync(order);
                    await _orderedProductRepository.CreateRangeAsync(order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>());

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

        public async Task<IEnumerable<Order>> GetByFilterAsync(Guid enterpriseId,
            OrderFilterRequestDTO filter,
            bool descending,
            int pageNumber,
            int pageSize)
        {
            if (enterpriseId == Guid.Empty)
                return [];

            ArgumentNullException.ThrowIfNull(filter);

            var status = filter.Status;
            var customerId = filter.CustomerId;
            var isActive = filter.IsActive;
            var channel = filter.Channel;
            var startDate = filter.StartDate?.Date;
            var endDateExclusive = filter.EndDate?.Date.AddDays(1);
            var search = SharedFunctions.EscapeLikePattern(filter.Search?.Trim() ?? string.Empty);
            var orderNumberSearch = StorefrontOrderNumber.ToSearchableIdFragment(search);
            var sortKey = filter.SortBy?.Trim().ToLowerInvariant() switch
            {
                "id" => (Expression<Func<Order, object>>)(o => o.Id),
                "customer" => o => o.Customer!.Name,
                "items" => o => o.OrderedProduct!.Count(),
                "total" => o => o.TotalValue,
                "status" => o => o.Status,
                _ => o => o.CreatedAt
            };

            return await _orderRepository.GetAllAsyncByFilter(
                descending,
                pageNumber,
                pageSize,
                x => x.EnterpriseId == enterpriseId &&
                    (!isActive.HasValue || x.IsActive == isActive.Value) &&
                    (!status.HasValue || x.Status == status.Value) &&
                    (!customerId.HasValue || x.CustomerId == customerId.Value) &&
                    (!channel.HasValue || x.Channel == channel.Value) &&
                    (search == string.Empty ||
                     (x.Customer != null && EF.Functions.Like(x.Customer.Name, $"%{search}%", "\\")) ||
                     EF.Functions.Like(x.Id.ToString(), $"%{orderNumberSearch}%", "\\")) &&
                    (!startDate.HasValue || x.CreatedAt >= startDate.Value) &&
                    (!endDateExclusive.HasValue || x.CreatedAt < endDateExclusive.Value),
                sortKey);
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

        public async Task<Order?> GetByExternalReferenceAsync(Guid enterpriseId, string externalReference)
        {
            if (enterpriseId == Guid.Empty || string.IsNullOrWhiteSpace(externalReference))
                return null;

            var (orders, _) = await _orderRepository.GetPagedAsync(
                order => order.EnterpriseId == enterpriseId
                         && order.ExternalReference == externalReference
                         && (order.IsActive == null || order.IsActive == true),
                pageNumber: 1,
                pageSize: 1);

            return orders.FirstOrDefault();
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

            order.TotalValue = computedTotal + Math.Max(order.ShippingValue, 0);

            if (order.PaymentScheduledDate.Date == DateTime.UtcNow.Date && !IsPaydayStatus(order.Status))
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
                    if (existentOrder.Channel == (int)EnumOrderChannel.Storefront)
                        _appDbContext?.StorefrontEvent.Add(StorefrontEvents.Create(StorefrontEvents.Refunded, existentOrder, now));

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
            return UpdateWithShipmentClaim(order, enterpriseId).Order;
        }

        public OrderUpdateResult UpdateWithShipmentClaim(Order order, Guid enterpriseId)
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

                    var lockedThrough = GetClosedThroughUtc(existentOrder.EnterpriseId ?? enterpriseId);
                    PeriodLock.Assert(lockedThrough, existentOrder.PaymentScheduledDate, "This order");
                    if (order.PaymentScheduledDate != default)
                        PeriodLock.Assert(lockedThrough, order.PaymentScheduledDate, "The new order date");

                    var oldContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(existentOrder, DateTime.UtcNow.Date);
                    var now = DateTime.UtcNow;
                    var trackingAfter = order.TrackingCode is null
                        ? existentOrder.TrackingCode
                        : string.IsNullOrWhiteSpace(order.TrackingCode) ? null : order.TrackingCode.Trim();
                    var trackingUrlAfter = order.TrackingUrl is null
                        ? existentOrder.TrackingUrl
                        : string.IsNullOrWhiteSpace(order.TrackingUrl) ? null : order.TrackingUrl.Trim();

                    var claimsShipmentTransition = existentOrder.Status != (int)EnumOrderStatus.Shipped
                        && order.Status == (int)EnumOrderStatus.Shipped;
                    var claimsTrackingCorrection = existentOrder.Status == (int)EnumOrderStatus.Shipped
                        && order.Status == (int)EnumOrderStatus.Shipped
                        && ((!string.Equals(existentOrder.TrackingCode, trackingAfter, StringComparison.Ordinal) && order.TrackingCode is not null)
                            || (!string.Equals(existentOrder.TrackingUrl, trackingUrlAfter, StringComparison.Ordinal) && order.TrackingUrl is not null));

                    if (_appDbContext != null && claimsShipmentTransition)
                    {
                        var claimedRows = _appDbContext.Database.ExecuteSqlInterpolated(
                            $"UPDATE [Order] SET UpdatedAt = {now}, Status = {(int)EnumOrderStatus.Shipped}, ShippedAt = COALESCE(ShippedAt, {now}) WHERE Id = {order.Id} AND EnterpriseId = {existentOrder.EnterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND Status = {existentOrder.Status} AND Status <> {(int)EnumOrderStatus.Refund} AND Status <> {(int)EnumOrderStatus.Finished} AND Status <> {(int)EnumOrderStatus.Shipped}");

                        if (claimedRows <= 0)
                        {
                            _appDbContext.Entry(existentOrder).Reload();
                            if (existentOrder.Status == (int)EnumOrderStatus.Shipped)
                            {
                                claimsShipmentTransition = false;
                                trackingAfter = order.TrackingCode is null
                                    ? existentOrder.TrackingCode
                                    : string.IsNullOrWhiteSpace(order.TrackingCode) ? null : order.TrackingCode.Trim();
                                trackingUrlAfter = order.TrackingUrl is null
                                    ? existentOrder.TrackingUrl
                                    : string.IsNullOrWhiteSpace(order.TrackingUrl) ? null : order.TrackingUrl.Trim();
                                claimsTrackingCorrection = order.Status == (int)EnumOrderStatus.Shipped
                                    && ((!string.Equals(existentOrder.TrackingCode, trackingAfter, StringComparison.Ordinal) && order.TrackingCode is not null)
                                        || (!string.Equals(existentOrder.TrackingUrl, trackingUrlAfter, StringComparison.Ordinal) && order.TrackingUrl is not null));

                                if (claimsTrackingCorrection)
                                    claimsTrackingCorrection = ClaimTrackingCorrection(
                                        order, existentOrder, trackingAfter, trackingUrlAfter, now);
                            }
                            else
                            {
                                throw new InsertDatabaseException("Order was concurrently finalized or already refunded.");
                            }
                        }
                    }
                    else if (_appDbContext != null && claimsTrackingCorrection)
                    {
                        claimsTrackingCorrection = ClaimTrackingCorrection(
                            order, existentOrder, trackingAfter, trackingUrlAfter, now);
                    }
                    else if (_appDbContext != null)
                    {
                        var guardedRows = _appDbContext.Database.ExecuteSqlInterpolated(
                            $"UPDATE [Order] SET UpdatedAt = {now} WHERE Id = {order.Id} AND EnterpriseId = {existentOrder.EnterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND Status = {existentOrder.Status} AND Status <> {(int)EnumOrderStatus.Refund} AND Status <> {(int)EnumOrderStatus.Finished}");

                        if (guardedRows <= 0)
                            throw new InsertDatabaseException("Order was concurrently finalized or already refunded.");
                    }

                    existentOrder.CustomerId = order.CustomerId ?? existentOrder.CustomerId;
                    existentOrder.UserId = order.UserId ?? existentOrder.UserId;
                    // A Limen order is paid before the store ever touches it: "em separação" (Processing) or
                    // shipping it must neither un-pay it nor move the day it was paid.
                    existentOrder.Payday = existentOrder.Channel == (int)EnumOrderChannel.Storefront && existentOrder.Payday.HasValue
                        ? existentOrder.Payday
                        : ResolvePaydayForStatus(
                            existentOrder.Payday,
                            order.Payday,
                            existentOrder.Status,
                            order.Status,
                            now);
                    if (order.PaymentScheduledDate != default)
                        existentOrder.PaymentScheduledDate = order.PaymentScheduledDate;

                    if (StorefrontEvents.ForUpdate(existentOrder, order.Status, trackingAfter) is { } shopperEvent)
                        _appDbContext?.StorefrontEvent.Add(StorefrontEvents.Create(shopperEvent, existentOrder, now));

                    existentOrder.Status = order.Status;
                    if (order.Status == (int)EnumOrderStatus.Shipped)
                        existentOrder.ShippedAt ??= now;
                    if (order.Status is (int)EnumOrderStatus.Delivered or (int)EnumOrderStatus.Finished)
                        existentOrder.DeliveredAt ??= now;
                    if (order.TrackingCode is not null)
                        existentOrder.TrackingCode = string.IsNullOrWhiteSpace(order.TrackingCode) ? null : order.TrackingCode.Trim();
                    if (order.TrackingUrl is not null)
                        existentOrder.TrackingUrl = string.IsNullOrWhiteSpace(order.TrackingUrl) ? null : order.TrackingUrl.Trim();
                    existentOrder.OrderedProduct = null;
                    existentOrder.UpdatedAt = now;

                    var updatedOrder = _orderRepository.Update(existentOrder);

                    var newContribution = EnterpriseBalanceContributionHelper.GetOrderRealizedContribution(updatedOrder, DateTime.UtcNow.Date);
                    _orderRepository.Commit();
                    ApplyEnterpriseBalanceDelta(updatedOrder.EnterpriseId ?? Guid.Empty, newContribution - oldContribution);

                    return new OrderUpdateResult(updatedOrder, claimsShipmentTransition, claimsTrackingCorrection);
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
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating the order.", ex);
            }
        }

        private bool ClaimTrackingCorrection(Order order, Order existentOrder, string? trackingAfter, string? trackingUrlAfter, DateTime now)
        {
            var claimedRows = _appDbContext!.Database.ExecuteSqlInterpolated(
                $"UPDATE [Order] SET UpdatedAt = {now}, TrackingCode = {trackingAfter}, TrackingUrl = {trackingUrlAfter} WHERE Id = {order.Id} AND EnterpriseId = {existentOrder.EnterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND Status = {(int)EnumOrderStatus.Shipped} AND ((TrackingCode = {existentOrder.TrackingCode}) OR (TrackingCode IS NULL AND {existentOrder.TrackingCode} IS NULL)) AND ((TrackingUrl = {existentOrder.TrackingUrl}) OR (TrackingUrl IS NULL AND {existentOrder.TrackingUrl} IS NULL))");

            if (claimedRows > 0)
                return true;

            _appDbContext.Entry(existentOrder).Reload();
            if (existentOrder.Status == (int)EnumOrderStatus.Shipped
                && string.Equals(existentOrder.TrackingCode, trackingAfter, StringComparison.Ordinal)
                && string.Equals(existentOrder.TrackingUrl, trackingUrlAfter, StringComparison.Ordinal))
                return false;

            throw new InsertDatabaseException("Order tracking was concurrently updated.");
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
                throw new DuplicateMessageException(messageId);
            }
        }

        private static bool IsUniqueViolation(DbUpdateException exception)
        {
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

        private async Task AssertWithinCreditLimitAsync(Order order, bool allowCreditOverride)
        {
            if (allowCreditOverride)
                return;

            var enterpriseId = order.EnterpriseId ?? Guid.Empty;
            var customerId = order.CustomerId ?? Guid.Empty;
            if (enterpriseId == Guid.Empty || customerId == Guid.Empty)
                return;

            var customer = (await _customerRepository.GetPagedAsync(
                    x => x.Id == customerId && x.EnterpriseId == enterpriseId))
                .Items
                .FirstOrDefault();

            var creditLimit = customer?.CreditLimit;
            if (creditLimit == null)
                return;

            await _customerRepository.LockForUpdateAsync(customerId);

            var exposure = await GetOpenExposureAsync(enterpriseId, customerId);
            var projected = exposure + order.TotalValue;
            if (projected <= creditLimit.Value)
                return;

            throw new CreditLimitExceededException(
                $"This order would take the customer to {projected:0.00} against a credit limit of {creditLimit.Value:0.00}.");
        }

        /// <summary>Everything this customer still owes, boleto first, refunds excluded.</summary>
        private async Task<decimal> GetOpenExposureAsync(Guid enterpriseId, Guid customerId)
        {
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

    }
}
