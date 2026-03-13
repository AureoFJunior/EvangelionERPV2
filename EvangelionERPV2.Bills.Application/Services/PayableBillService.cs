using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class PayableBillService : IPayableBillService
    {
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<PayableBillProduct> _payableBillProductRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<OrderedProduct> _orderedProductRepository;

        public PayableBillService(
            IRepository<PayableBill> payableBillRepository,
            IRepository<PayableBillProduct> payableBillProductRepository,
            IRepository<Product> productRepository,
            IRepository<Order> orderRepository,
            IRepository<OrderedProduct> orderedProductRepository)
        {
            _payableBillRepository = payableBillRepository;
            _payableBillProductRepository = payableBillProductRepository;
            _productRepository = productRepository;
            _orderRepository = orderRepository;
            _orderedProductRepository = orderedProductRepository;
        }

        public async Task<PayableBill> CreateAsync(PayableBill payableBill)
        {
            if (payableBill == null)
                throw new InsertDatabaseException($"{nameof(PayableBill)} is null.");

            payableBill.Id = payableBill.Id == Guid.Empty ? Guid.NewGuid() : payableBill.Id;
            payableBill.CreatedAt = DateTime.UtcNow;
            payableBill.UpdatedAt = DateTime.UtcNow;
            payableBill.IsActive = true;

            var hasProvidedItems = payableBill.Items?.Any() == true;
            var normalizedItems = hasProvidedItems
                ? await NormalizeItemsAsync(payableBill.Id, payableBill.EnterpriseId, payableBill.Items!)
                : new List<PayableBillProduct>();

            if (hasProvidedItems)
                payableBill.Amount = normalizedItems.Sum(x => x.LineAmount);

            payableBill.Items = null;

            await _payableBillRepository.CreateAsync(payableBill);

            if (hasProvidedItems && normalizedItems.Any())
                await _payableBillProductRepository.CreateRangeAsync(normalizedItems);

            await _payableBillRepository.CommitAsync();

            var createdBill = await GetByIdAsync(payableBill.Id, payableBill.EnterpriseId);
            return createdBill ?? payableBill;
        }

        public async Task<IEnumerable<PayableBill>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null)
        {
            var bills = (await _payableBillRepository.GetAllAsync(
                pageNumber,
                pageSize,
                x => x.EnterpriseId == enterpriseId && x.IsActive == true)).ToList();

            await PopulateItemsAsync(bills);
            return bills;
        }

        public async Task<PayableBill?> GetByIdAsync(Guid id, Guid enterpriseId)
        {
            var bill = await _payableBillRepository.GetByIdAsync(id);
            if (bill == null || bill.EnterpriseId != enterpriseId || bill.IsActive != true)
                return null;

            await PopulateItemsAsync([bill]);
            return bill;
        }

        public async Task<PayableBill> UpdateAsync(PayableBill payableBill, Guid enterpriseId)
        {
            var existing = await _payableBillRepository.GetByIdAsync(payableBill.Id);
            if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

            var hasProvidedItemsPayload = payableBill.Items != null;
            var hasProvidedItems = payableBill.Items?.Any() == true;

            if (existing.ProductsReceivedAt.HasValue && hasProvidedItemsPayload)
                throw new InsertDatabaseException("Products were already received. Item lines cannot be changed.");

            var normalizedItems = hasProvidedItems
                ? await NormalizeItemsAsync(existing.Id, enterpriseId, payableBill.Items!)
                : new List<PayableBillProduct>();

            existing.Description = payableBill.Description;
            existing.DueDate = payableBill.DueDate;
            existing.PaidAt = payableBill.PaidAt;
            existing.IsPaid = payableBill.IsPaid;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Amount = hasProvidedItems ? normalizedItems.Sum(x => x.LineAmount) : payableBill.Amount;

            _payableBillRepository.Update(existing);

            if (hasProvidedItemsPayload)
            {
                await SoftDeleteAllItemsAsync(existing.Id);
                if (normalizedItems.Any())
                    await _payableBillProductRepository.CreateRangeAsync(normalizedItems);
            }

            await _payableBillRepository.CommitAsync();

            var updated = await GetByIdAsync(existing.Id, enterpriseId);
            return updated ?? existing;
        }

        public async Task<PayableBill> MarkProductsReceivedAsync(Guid id, Guid enterpriseId)
        {
            var existing = await _payableBillRepository.GetByIdAsync(id);
            if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

            if (existing.ProductsReceivedAt.HasValue)
                throw new InsertDatabaseException("Products were already received for this payable bill.");

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
                product.UpdatedAt = DateTime.UtcNow;
                _productRepository.Update(product);
            }

            existing.ProductsReceivedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            _payableBillRepository.Update(existing);

            await _productRepository.CommitAsync();

            var receivedBill = await GetByIdAsync(existing.Id, enterpriseId);
            return receivedBill ?? existing;
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
                orders = (await _orderRepository.GetAllAsync(
                        x => x.IsActive == true
                             && x.EnterpriseId == enterpriseId
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
            var existing = await _payableBillRepository.GetByIdAsync(id);
            if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

            if (existing.ProductsReceivedAt.HasValue)
                throw new InsertDatabaseException("Products were already received. Item lines cannot be deleted.");

            existing.IsActive = false;
            existing.UpdatedAt = DateTime.UtcNow;
            _payableBillRepository.Update(existing);

            await SoftDeleteAllItemsAsync(existing.Id);
            await _payableBillRepository.CommitAsync();

            var deleted = await GetByIdAsync(existing.Id, enterpriseId);
            return deleted ?? existing;
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
    }
}
