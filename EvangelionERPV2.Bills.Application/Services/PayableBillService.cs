using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class PayableBillService : IPayableBillService
    {
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<PayableBillProduct> _payableBillProductRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<OrderedProduct> _orderedProductRepository;
        private readonly AppDbContext? _appDbContext;

        public PayableBillService(
            IRepository<PayableBill> payableBillRepository,
            IRepository<PayableBillProduct> payableBillProductRepository,
            IRepository<Product> productRepository,
            IRepository<Enterprise> enterpriseRepository,
            IRepository<Order> orderRepository,
            IRepository<OrderedProduct> orderedProductRepository,
            AppDbContext? appDbContext = null)
        {
            _payableBillRepository = payableBillRepository;
            _payableBillProductRepository = payableBillProductRepository;
            _productRepository = productRepository;
            _enterpriseRepository = enterpriseRepository;
            _orderRepository = orderRepository;
            _orderedProductRepository = orderedProductRepository;
            _appDbContext = appDbContext;
        }

        public async Task<PayableBill> CreateAsync(PayableBill payableBill)
        {
            if (payableBill == null)
                throw new InsertDatabaseException($"{nameof(PayableBill)} is null.");

            if (payableBill.EnterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Payable bill enterprise is required.");

            var sourceItems = payableBill.Items?.ToList() ?? [];
            payableBill.Id = Guid.NewGuid();

            try
            {
                await ExecuteInTransactionAsync(async () =>
                {
                    payableBill.CreatedAt = DateTime.UtcNow;
                    payableBill.UpdatedAt = DateTime.UtcNow;
                    payableBill.IsActive = true;
                    payableBill.RefundReason = null;
                    payableBill.RefundedAt = null;
                    payableBill.BillType = NormalizeBillType(payableBill.BillType);
                    payableBill.PaidAt = payableBill.IsPaid
                        ? (payableBill.PaidAt ?? DateTime.UtcNow)
                        : null;

                    var hasProvidedItems = sourceItems.Any();
                    var normalizedItems = hasProvidedItems
                        ? await NormalizeItemsAsync(payableBill.Id, payableBill.EnterpriseId, sourceItems)
                        : new List<PayableBillProduct>();

                    if (hasProvidedItems)
                        payableBill.Amount = normalizedItems.Sum(x => x.LineAmount);
                    else
                        payableBill.Amount = payableBill.Amount > 0 ? payableBill.Amount : 0;

                    payableBill.Items = null;

                    await _payableBillRepository.CreateAsync(payableBill);

                    if (hasProvidedItems && normalizedItems.Any())
                        await _payableBillProductRepository.CreateRangeAsync(normalizedItems);

                    await _payableBillRepository.CommitAsync();

                    await ApplyEnterpriseBalanceDeltaAsync(
                        payableBill.EnterpriseId,
                        EnterpriseBalanceContributionHelper.GetPayableRealizedContribution(payableBill, DateTime.UtcNow.Date));
                });
            }
            catch (DbUpdateException ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating payable bill.", ex);
            }

            var createdBill = await GetByIdAsync(payableBill.Id, payableBill.EnterpriseId);
            return createdBill ?? payableBill;
        }

        public async Task<IEnumerable<PayableBill>> GetByEnterpriseIdAsync(
            Guid enterpriseId,
            int? pageNumber = null,
            int? pageSize = null,
            bool? isActive = true,
            int? billType = null)
        {
            var bills = (await _payableBillRepository.GetAllAsync(
                pageNumber,
                pageSize,
                x => x.EnterpriseId == enterpriseId
                     && (!isActive.HasValue || x.IsActive == isActive.Value)
                     && (!billType.HasValue || x.BillType == billType.Value))).ToList();

            NormalizeBillTypesOnRead(bills);
            await PopulateItemsAsync(bills);
            return bills;
        }

        public async Task<PayableBill?> GetByIdAsync(Guid id, Guid enterpriseId)
        {
            var bill = await _payableBillRepository.GetByIdAsync(id);
            if (bill == null || bill.EnterpriseId != enterpriseId || bill.IsActive != true)
                return null;

            bill.BillType = SanitizeBillTypeOnRead(bill.BillType);
            await PopulateItemsAsync([bill]);
            return bill;
        }

        public async Task<PayableBill> UpdateAsync(PayableBill payableBill, Guid enterpriseId)
        {
            if (payableBill == null)
                throw new InsertDatabaseException($"{nameof(PayableBill)} is null.");

            PayableBill? updatedEntity = null;

            await ExecuteInTransactionAsync(async () =>
            {
                var existing = await _payableBillRepository.GetByIdAsync(payableBill.Id);
                if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                    throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

                if (existing.RefundedAt.HasValue)
                    throw new InsertDatabaseException("Payable bill is refunded and cannot be edited.");

                var oldContribution = EnterpriseBalanceContributionHelper.GetPayableRealizedContribution(existing, DateTime.UtcNow.Date);
                var now = DateTime.UtcNow;

                var hasProvidedItemsPayload = payableBill.Items != null;
                var hasProvidedItems = payableBill.Items?.Any() == true;

                if (existing.ProductsReceivedAt.HasValue && hasProvidedItemsPayload)
                    throw new InsertDatabaseException("Products were already received. Item lines cannot be changed.");

                if (hasProvidedItemsPayload && _appDbContext != null)
                {
                    var guardedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE PayableBill SET UpdatedAt = {now} WHERE Id = {existing.Id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND ProductsReceivedAt IS NULL");

                    if (guardedRows <= 0)
                        throw new InsertDatabaseException("Payable bill was concurrently finalized.");
                }

                var normalizedItems = hasProvidedItems
                    ? await NormalizeItemsAsync(existing.Id, enterpriseId, payableBill.Items!)
                    : new List<PayableBillProduct>();

                existing.Description = payableBill.Description;
                existing.DueDate = payableBill.DueDate;
                existing.IsPaid = payableBill.IsPaid;
                existing.PaidAt = payableBill.IsPaid
                    ? (payableBill.PaidAt ?? now)
                    : null;
                existing.BillType = NormalizeBillType(payableBill.BillType);
                existing.UpdatedAt = now;
                existing.Amount = hasProvidedItems ? normalizedItems.Sum(x => x.LineAmount) : payableBill.Amount;
                existing.RefundReason = null;
                existing.RefundedAt = null;

                _payableBillRepository.Update(existing);

                if (hasProvidedItemsPayload)
                {
                    await SoftDeleteAllItemsAsync(existing.Id);
                    if (normalizedItems.Any())
                        await _payableBillProductRepository.CreateRangeAsync(normalizedItems);
                }

                await _payableBillRepository.CommitAsync();

                var newContribution = EnterpriseBalanceContributionHelper.GetPayableRealizedContribution(existing, DateTime.UtcNow.Date);
                await ApplyEnterpriseBalanceDeltaAsync(existing.EnterpriseId, newContribution - oldContribution);
                updatedEntity = existing;
            });

            var updated = await GetByIdAsync(payableBill.Id, enterpriseId);
            return updated ?? updatedEntity ?? payableBill;
        }

        public async Task<PayableBill> MarkProductsReceivedAsync(Guid id, Guid enterpriseId)
        {
            PayableBill? receivedEntity = null;

            await ExecuteInTransactionAsync(async () =>
            {
                var existing = await _payableBillRepository.GetByIdAsync(id);
                if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                    throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

                if (existing.RefundedAt.HasValue)
                    throw new InsertDatabaseException("Refunded payable bills cannot receive products.");

                if (existing.ProductsReceivedAt.HasValue)
                    throw new InsertDatabaseException("Products were already received for this payable bill.");

                var now = DateTime.UtcNow;
                if (_appDbContext != null)
                {
                    var affectedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE PayableBill SET ProductsReceivedAt = {now}, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND ProductsReceivedAt IS NULL");

                    if (affectedRows <= 0)
                        throw new InsertDatabaseException("Products were already received for this payable bill.");
                }

                var items = (await _payableBillProductRepository.GetAllAsync(
                        x => x.IsActive == true && x.PayableBillId == id))
                    .ToList();

                if (!items.Any())
                    throw new InsertDatabaseException("Payable bill has no product items to receive.");

                var productIds = items.Select(x => x.ProductId).Distinct().ToList();
                var products = (await _productRepository.GetAllAsync(
                        x => x.IsActive == true
                             && x.EnterpriseId == enterpriseId
                             && productIds.Contains(x.Id)))
                    .ToDictionary(x => x.Id, x => x);

                if (products.Count != productIds.Count)
                    throw new InsertDatabaseException("Some products were not found for this payable bill.");

                foreach (var item in items)
                {
                    if (!products.TryGetValue(item.ProductId, out var product))
                        throw new InsertDatabaseException($"Product [{item.ProductId}] was not found.");

                    product.StorageQuantity += item.Quantity;
                    product.UpdatedAt = now;
                    _productRepository.Update(product);
                }

                existing.ProductsReceivedAt = now;
                existing.UpdatedAt = now;
                _payableBillRepository.Update(existing);

                await _productRepository.CommitAsync();
                receivedEntity = existing;
            });

            var receivedBill = await GetByIdAsync(id, enterpriseId);
            return receivedBill ?? receivedEntity
                ?? throw new InsertDatabaseException("Payable bill receipt failed.");
        }

        public async Task<PayableBill> RefundAsync(Guid id, Guid enterpriseId, string reason)
        {
            PayableBill? refundedEntity = null;

            await ExecuteInTransactionAsync(async () =>
            {
                var existing = await _payableBillRepository.GetByIdAsync(id);
                if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                    throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

                if (existing.RefundedAt.HasValue)
                    throw new InsertDatabaseException("Payable bill was already refunded.");

                var oldContribution = EnterpriseBalanceContributionHelper.GetPayableRealizedContribution(existing, DateTime.UtcNow.Date);

                var trimmedReason = (reason ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmedReason))
                    throw new InsertDatabaseException("Refund reason is required.");

                var now = DateTime.UtcNow;
                if (_appDbContext != null)
                {
                    var affectedRows = existing.ProductsReceivedAt.HasValue
                        ? await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE PayableBill SET Amount = 0, IsPaid = 0, PaidAt = NULL, RefundReason = {trimmedReason}, RefundedAt = {now}, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND ProductsReceivedAt IS NOT NULL")
                        : await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE PayableBill SET Amount = 0, IsPaid = 0, PaidAt = NULL, RefundReason = {trimmedReason}, RefundedAt = {now}, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND ProductsReceivedAt IS NULL");

                    if (affectedRows <= 0)
                        throw new InsertDatabaseException("Payable bill was already refunded or concurrently finalized.");
                }

                var items = (await _payableBillProductRepository.GetAllAsync(
                        x => x.IsActive == true && x.PayableBillId == id))
                    .ToList();

                if (!items.Any())
                    throw new InsertDatabaseException("Refund is only allowed when payable bill has product items.");

                if (existing.ProductsReceivedAt.HasValue)
                {
                    var productIds = items.Select(x => x.ProductId).Distinct().ToList();
                    var products = (await _productRepository.GetAllAsync(
                            x => x.IsActive == true
                                 && x.EnterpriseId == enterpriseId
                                 && productIds.Contains(x.Id)))
                        .ToDictionary(x => x.Id, x => x);

                    if (products.Count != productIds.Count)
                        throw new InsertDatabaseException("Some products were not found for this payable bill.");

                    foreach (var item in items)
                    {
                        if (!products.TryGetValue(item.ProductId, out var product))
                            throw new InsertDatabaseException($"Product [{item.ProductId}] was not found.");

                        product.StorageQuantity -= Math.Max(0, item.Quantity);
                        if (product.StorageQuantity < 0)
                            product.StorageQuantity = 0;

                        product.UpdatedAt = now;
                        _productRepository.Update(product);
                    }
                }

                foreach (var item in items)
                {
                    item.Quantity = 0;
                    item.UnitValue = 0;
                    item.LineAmount = 0;
                    item.UpdatedAt = now;
                }

                _payableBillProductRepository.UpdateRange(items);

                existing.Amount = 0;
                existing.IsPaid = false;
                existing.PaidAt = null;
                existing.RefundReason = trimmedReason;
                existing.RefundedAt = now;
                existing.UpdatedAt = now;
                _payableBillRepository.Update(existing);

                var newContribution = EnterpriseBalanceContributionHelper.GetPayableRealizedContribution(existing, now.Date);
                await ApplyEnterpriseBalanceDeltaAsync(existing.EnterpriseId, newContribution - oldContribution);

                await _payableBillRepository.CommitAsync();
                refundedEntity = existing;
            });

            var refundedBill = await GetByIdAsync(id, enterpriseId);
            return refundedBill ?? refundedEntity
                ?? throw new InsertDatabaseException("Payable bill refund failed.");
        }

        public async Task<IEnumerable<ReplenishmentSuggestionDTO>> GetReplenishmentSuggestionsAsync(Guid enterpriseId, ReplenishmentSuggestionRequestDTO request)
        {
            request ??= new ReplenishmentSuggestionRequestDTO();
            var historyWindowDays = request.HistoryWindowDays <= 0 ? 180 : request.HistoryWindowDays;
            var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
            var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;

            var windowStart = DateTime.UtcNow.Date.AddDays(-historyWindowDays);

            List<Order> orders;
            try
            {
                var finalizedStatuses = new HashSet<int>
                {
                    (int)EnumOrderStatus.Paid,
                    (int)EnumOrderStatus.Shipped,
                    (int)EnumOrderStatus.Delivered,
                    (int)EnumOrderStatus.Finished
                };

                orders = (await _orderRepository.GetAllAsync(
                        x => x.IsActive == true
                              && x.EnterpriseId == enterpriseId
                              && !x.RefundedAt.HasValue
                              && finalizedStatuses.Contains(x.Status)
                              && x.CreatedAt >= windowStart))
                    .ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return [];
            }

            if (!orders.Any())
                return [];

            var orderIds = orders.Select(x => x.Id).ToHashSet();
            var ordersById = orders.ToDictionary(x => x.Id, x => x);

            List<OrderedProduct> orderedProducts;
            try
            {
                orderedProducts = (await _orderedProductRepository.GetAllAsync(
                        x => x.IsActive == true
                             && x.OrderId.HasValue
                             && orderIds.Contains(x.OrderId.Value)))
                    .ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return [];
            }

            if (!orderedProducts.Any())
                return [];

            var productIds = orderedProducts.Select(x => x.ProductId).Distinct().ToList();
            Dictionary<Guid, Product> productsById;
            try
            {
                productsById = (await _productRepository.GetAllAsync(
                        x => x.IsActive == true
                             && x.EnterpriseId == enterpriseId
                             && productIds.Contains(x.Id)))
                    .ToDictionary(x => x.Id, x => x);
            }
            catch (NotFoundDatabaseException)
            {
                return [];
            }

            var suggestions = new List<ReplenishmentSuggestionDTO>();

            foreach (var skuGroup in orderedProducts.GroupBy(x => x.ProductId))
            {
                if (!productsById.TryGetValue(skuGroup.Key, out var product))
                    continue;

                var groupedLines = skuGroup
                    .Where(x => x.OrderId.HasValue && ordersById.ContainsKey(x.OrderId!.Value))
                    .ToList();

                if (!groupedLines.Any())
                    continue;

                var intervals = BuildIntervals(groupedLines, ordersById);
                if (intervals.Count < 3)
                    continue;

                var totalSold = groupedLines.Sum(x => x.Quantity);
                if (totalSold <= 0)
                    continue;

                var dailyConsumption = totalSold / historyWindowDays;
                if (dailyConsumption <= 0)
                    continue;

                var leadTimeDays = Math.Max(1, (int)Math.Round(GetPercentile(intervals, 0.5)));
                var minCoverageDays = Math.Max(1, (int)Math.Round(GetPercentile(intervals, 0.25)));
                var maxCoverageDays = Math.Max(
                    minCoverageDays + 1,
                    (int)Math.Round(GetPercentile(intervals, 0.75) + leadTimeDays));

                var currentStock = Math.Max(0, product.StorageQuantity);
                var coverageDays = currentStock / dailyConsumption;

                var targetStock = dailyConsumption * (leadTimeDays + minCoverageDays);
                var suggestedQuantity = Math.Max(0, targetStock - currentStock);

                var (alert, criticality) = ResolveAlertAndCriticality(coverageDays, minCoverageDays, maxCoverageDays);

                suggestions.Add(new ReplenishmentSuggestionDTO
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    CurrentStock = Math.Round(currentStock, 2),
                    DailyConsumption = Math.Round(dailyConsumption, 4),
                    LeadTimeDays = leadTimeDays,
                    MinCoverageDays = minCoverageDays,
                    MaxCoverageDays = maxCoverageDays,
                    CoverageDays = Math.Round(coverageDays, 2),
                    SuggestedQuantity = Math.Round(suggestedQuantity, 2),
                    Alert = alert,
                    Criticality = criticality
                });
            }

            var query = request.SortByCriticality
                ? suggestions
                    .OrderBy(x => GetCriticalityRank(x.Criticality))
                    .ThenBy(x => x.CoverageDays)
                    .ThenBy(x => x.ProductName)
                : suggestions.OrderBy(x => x.ProductName);

            return query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        public async Task<PayableBill> DeleteAsync(Guid id, Guid enterpriseId)
        {
            PayableBill? deletedEntity = null;

            await ExecuteInTransactionAsync(async () =>
            {
                var existing = await _payableBillRepository.GetByIdAsync(id);
                if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                    throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

                var existingItems = (await _payableBillProductRepository.GetAllAsync(
                        x => x.IsActive == true && x.PayableBillId == existing.Id))
                    .ToList();

                var oldContribution = EnterpriseBalanceContributionHelper.GetPayableRealizedContribution(existing, DateTime.UtcNow.Date);
                var now = DateTime.UtcNow;

                if (_appDbContext != null)
                {
                    var affectedRows = existing.ProductsReceivedAt.HasValue
                        ? await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE PayableBill SET IsActive = 0, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND ProductsReceivedAt IS NOT NULL")
                        : await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE PayableBill SET IsActive = 0, UpdatedAt = {now} WHERE Id = {id} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND RefundedAt IS NULL AND ProductsReceivedAt IS NULL");

                    if (affectedRows <= 0)
                        throw new InsertDatabaseException("Payable bill was concurrently updated or already finalized.");
                }

                if (existing.ProductsReceivedAt.HasValue && existingItems.Any())
                {
                    var productIds = existingItems.Select(x => x.ProductId).Distinct().ToList();
                    var productsById = (await _productRepository.GetAllAsync(
                            x => x.IsActive == true
                                 && x.EnterpriseId == enterpriseId
                                 && productIds.Contains(x.Id)))
                        .ToDictionary(x => x.Id, x => x);

                    if (productsById.Count != productIds.Count)
                        throw new InsertDatabaseException("Some products for this payable bill were not found.");

                    foreach (var item in existingItems)
                    {
                        if (!productsById.TryGetValue(item.ProductId, out var product))
                            throw new InsertDatabaseException($"Product [{item.ProductId}] was not found.");

                        product.StorageQuantity -= Math.Max(0, item.Quantity);
                        if (product.StorageQuantity < 0)
                            product.StorageQuantity = 0;

                        product.UpdatedAt = now;
                    }

                    _productRepository.UpdateRange(productsById.Values);
                }

                existing.IsActive = false;
                existing.UpdatedAt = now;
                _payableBillRepository.Update(existing);

                await SoftDeleteAllItemsAsync(existing.Id);
                await _payableBillRepository.CommitAsync();

                await ApplyEnterpriseBalanceDeltaAsync(existing.EnterpriseId, -oldContribution);
                deletedEntity = existing;
            });

            var deleted = await GetByIdAsync(id, enterpriseId);
            return deleted ?? deletedEntity
                ?? throw new InsertDatabaseException("Payable bill deletion failed.");
        }

        private async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            await _payableBillRepository.ExecuteInTransactionAsync(operation);
        }

        private async Task<List<PayableBillProduct>> NormalizeItemsAsync(Guid payableBillId, Guid enterpriseId, IEnumerable<PayableBillProduct> items)
        {
            var sourceItems = items?.ToList() ?? [];
            if (!sourceItems.Any())
                return [];

            if (sourceItems.Any(x => x.Quantity <= 0))
                throw new InsertDatabaseException("Each payable item must have quantity greater than zero.");

            var distinctProductIds = sourceItems.Select(x => x.ProductId).Distinct().ToList();
            if (distinctProductIds.Count != sourceItems.Count)
                throw new InsertDatabaseException("Payable bill has duplicate products.");

            var products = (await _productRepository.GetAllAsync(
                    x => x.IsActive == true
                         && x.EnterpriseId == enterpriseId
                         && distinctProductIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => x);

            if (products.Count != distinctProductIds.Count)
                throw new InsertDatabaseException("Some payable bill products were not found in enterprise inventory.");

            var now = DateTime.UtcNow;

            return sourceItems.Select(item =>
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                    throw new InsertDatabaseException($"Product [{item.ProductId}] was not found.");

                var resolvedUnitValue = item.UnitValue > 0 ? item.UnitValue : Math.Max(0, product.DefaultValue);
                var lineAmount = item.Quantity * resolvedUnitValue;

                return new PayableBillProduct
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = payableBillId,
                    ProductId = product.Id,
                    Quantity = item.Quantity,
                    UnitValue = resolvedUnitValue,
                    LineAmount = Math.Round(lineAmount, 2),
                    UnitOfMeasure = product.UnitOfMeasure,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsActive = true
                };
            }).ToList();
        }

        private async Task PopulateItemsAsync(IEnumerable<PayableBill> bills)
        {
            var billList = bills.ToList();
            if (!billList.Any())
                return;

            var billIds = billList.Select(x => x.Id).Distinct().ToList();
            var items = (await _payableBillProductRepository.GetAllAsync(
                    x => x.IsActive == true && billIds.Contains(x.PayableBillId)))
                .ToList();

            if (!items.Any())
            {
                foreach (var bill in billList)
                    bill.Items = [];
                return;
            }

            var productIds = items.Select(x => x.ProductId).Distinct().ToList();
            var products = (await _productRepository.GetAllAsync(
                    x => x.IsActive == true && productIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => x);

            foreach (var item in items)
            {
                if (products.TryGetValue(item.ProductId, out var product))
                    item.Product = product;
            }

            var itemsByBillId = items
                .GroupBy(x => x.PayableBillId)
                .ToDictionary(x => x.Key, x => (ICollection<PayableBillProduct>)x.ToList());

            foreach (var bill in billList)
            {
                bill.Items = itemsByBillId.TryGetValue(bill.Id, out var billItems) ? billItems : [];
            }
        }

        private async Task SoftDeleteAllItemsAsync(Guid payableBillId)
        {
            var existingItems = (await _payableBillProductRepository.GetAllAsync(
                    x => x.IsActive == true && x.PayableBillId == payableBillId))
                .ToList();

            if (!existingItems.Any())
                return;

            foreach (var item in existingItems)
            {
                item.IsActive = false;
                item.UpdatedAt = DateTime.UtcNow;
            }

            _payableBillProductRepository.UpdateRange(existingItems);
        }

        private static List<int> BuildIntervals(IEnumerable<OrderedProduct> orderedProducts, IReadOnlyDictionary<Guid, Order> ordersById)
        {
            var demandDates = orderedProducts
                .Where(x => x.OrderId.HasValue && ordersById.ContainsKey(x.OrderId!.Value))
                .Select(x => ordersById[x.OrderId!.Value].CreatedAt.Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var intervals = new List<int>();
            for (var i = 1; i < demandDates.Count; i++)
            {
                var rawInterval = (demandDates[i] - demandDates[i - 1]).TotalDays;
                intervals.Add(Math.Max(1, (int)Math.Round(rawInterval)));
            }

            return intervals;
        }

        private static double GetPercentile(IReadOnlyList<int> values, double percentile)
        {
            if (values == null || values.Count == 0)
                return 0;

            var sorted = values.OrderBy(x => x).ToList();
            if (sorted.Count == 1)
                return sorted[0];

            var clampedPercentile = Math.Clamp(percentile, 0, 1);
            var rank = (sorted.Count - 1) * clampedPercentile;
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            var weight = rank - lower;

            if (lower == upper)
                return sorted[lower];

            return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
        }

        private static (string alert, string criticality) ResolveAlertAndCriticality(double coverageDays, int minCoverageDays, int maxCoverageDays)
        {
            if (coverageDays < minCoverageDays)
                return ("stockout", "high");

            if (coverageDays > maxCoverageDays)
                return ("overstock", "medium");

            return ("none", "low");
        }

        private static int GetCriticalityRank(string criticality)
        {
            return criticality?.ToLowerInvariant() switch
            {
                "high" => 0,
                "medium" => 1,
                _ => 2
            };
        }

        private static int NormalizeBillType(int billType)
        {
            if (!Enum.IsDefined(typeof(EnumPayableBillType), billType))
                throw new InsertDatabaseException("Invalid payable bill type.");

            return billType;
        }

        private static void NormalizeBillTypesOnRead(IEnumerable<PayableBill> bills)
        {
            foreach (var bill in bills)
                bill.BillType = SanitizeBillTypeOnRead(bill.BillType);
        }

        private static int SanitizeBillTypeOnRead(int billType)
        {
            return Enum.IsDefined(typeof(EnumPayableBillType), billType)
                ? billType
                : (int)EnumPayableBillType.Other;
        }

        private async Task ApplyEnterpriseBalanceDeltaAsync(Guid enterpriseId, double delta)
        {
            if (Math.Abs(delta) < 0.000001)
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
    }
}
