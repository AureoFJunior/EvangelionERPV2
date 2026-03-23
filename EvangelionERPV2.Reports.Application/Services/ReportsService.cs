using System.Text.Json;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ReportsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Caching.Distributed;

namespace EvangelionERPV2.ReportsModule.Application.Services
{
    public class ReportsService : IReportsService
    {
        private const int MaxReportsPerUser = 50;
        private static readonly TimeSpan ReportsTtl = TimeSpan.FromDays(1);
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly IDistributedCache _cache;
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly IProductReportGeneratorService _productReportGeneratorService;
        private readonly IOrderReportGeneratorService _orderReportGeneratorService;
        private readonly IPayableBillReportGeneratorService _payableBillReportGeneratorService;
        private readonly IOrderRepository<Order> _orderRepository;

        public ReportsService(
            IDistributedCache cache,
            IRepository<Enterprise> enterpriseRepository,
            IProductReportGeneratorService productReportGeneratorService,
            IOrderReportGeneratorService orderReportGeneratorService,
            IPayableBillReportGeneratorService payableBillReportGeneratorService,
            IOrderRepository<Order> orderRepository)
        {
            _cache = cache;
            _enterpriseRepository = enterpriseRepository;
            _productReportGeneratorService = productReportGeneratorService;
            _orderReportGeneratorService = orderReportGeneratorService;
            _payableBillReportGeneratorService = payableBillReportGeneratorService;
            _orderRepository = orderRepository;
        }

        public async Task<IEnumerable<ReportListItemDTO>> GetUserReportsAsync(Guid enterpriseId, Guid userId)
        {
            EnsureContextIds(enterpriseId, userId);
            var listKey = GetUserListKey(enterpriseId, userId);
            var reports = await GetListAsync(listKey);

            return reports
                .OrderByDescending(item => item.Date)
                .ToList();
        }

        public async Task<ReportListItemDTO> GenerateAsync(Guid enterpriseId, Guid userId, EnumReportType type)
        {
            EnsureContextIds(enterpriseId, userId);

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            if (enterprise == null || enterprise.IsActive != true)
                throw new NotFoundDatabaseException("Enterprise was not found.");

            var now = DateTime.UtcNow;
            var reportId = Guid.NewGuid();
            var (title, description, icon, htmlContent) = await BuildReportContentAsync(enterprise, type);

            if (string.IsNullOrWhiteSpace(htmlContent))
                throw new InsertDatabaseException("No data available to generate report.");

            var listItem = new ReportListItemDTO
            {
                Id = reportId,
                Title = title,
                Description = description,
                Date = now,
                Type = type,
                Icon = icon,
            };

            var detail = new ReportDetailDTO
            {
                Id = listItem.Id,
                Title = listItem.Title,
                Description = listItem.Description,
                Date = listItem.Date,
                Type = listItem.Type,
                Icon = listItem.Icon,
                HtmlContent = htmlContent,
            };

            var itemKey = GetReportItemKey(enterpriseId, userId, reportId);
            await SetCacheValueAsync(itemKey, detail);

            var listKey = GetUserListKey(enterpriseId, userId);
            var reports = await GetListAsync(listKey);
            reports.Insert(0, listItem);

            var normalizedReports = reports
                .OrderByDescending(item => item.Date)
                .Take(MaxReportsPerUser)
                .ToList();

            await SetCacheValueAsync(listKey, normalizedReports);

            return listItem;
        }

        public async Task<ReportDetailDTO?> GetByIdAsync(Guid enterpriseId, Guid userId, Guid reportId)
        {
            EnsureContextIds(enterpriseId, userId);
            if (reportId == Guid.Empty)
                return null;

            var itemKey = GetReportItemKey(enterpriseId, userId, reportId);
            var detail = await GetCacheValueAsync<ReportDetailDTO>(itemKey);
            if (detail == null)
                return null;

            if (detail.Id != reportId)
                return null;

            return detail;
        }

        private async Task<(string title, string description, string icon, string htmlContent)> BuildReportContentAsync(
            Enterprise enterprise,
            EnumReportType type)
        {
            return type switch
            {
                EnumReportType.Stock => (
                    "Stock Report",
                    "Current stock status and quantities by product",
                    "package",
                    await _productReportGeneratorService.GenerateStockReportAsync(enterprise)),
                EnumReportType.MonthlyBilling => await BuildMonthlyBillingContentAsync(enterprise),
                EnumReportType.TopProductsRevenue => await BuildTopProductsRevenueContentAsync(enterprise),
                EnumReportType.SalesByStatus => await BuildSalesByStatusContentAsync(enterprise),
                EnumReportType.PayablesOverview => await BuildPayablesOverviewContentAsync(enterprise),
                _ => throw new InsertDatabaseException("Invalid report type."),
            };
        }

        private async Task<(string title, string description, string icon, string htmlContent)> BuildMonthlyBillingContentAsync(Enterprise enterprise)
        {
            var orderList = await GetCurrentMonthOrdersOrThrowAsync(enterprise);
            var htmlContent = await _orderReportGeneratorService.GenerateMonthlyBillingReportAsync(enterprise, orderList);
            return (
                "Monthly Billing Report",
                "Orders billed during the current month",
                "bar-chart-2",
                htmlContent);
        }

        private async Task<(string title, string description, string icon, string htmlContent)> BuildTopProductsRevenueContentAsync(Enterprise enterprise)
        {
            var orderList = await GetCurrentMonthOrdersOrThrowAsync(enterprise);
            var htmlContent = await _orderReportGeneratorService.GenerateTopProductsByRevenueReportAsync(enterprise, orderList);

            return (
                "Top Products by Revenue",
                "Top selling products ranked by revenue in the current month",
                "trending-up",
                htmlContent);
        }

        private async Task<(string title, string description, string icon, string htmlContent)> BuildSalesByStatusContentAsync(Enterprise enterprise)
        {
            var orderList = await GetCurrentMonthOrdersOrThrowAsync(enterprise);
            var htmlContent = await _orderReportGeneratorService.GenerateSalesByStatusReportAsync(enterprise, orderList);

            return (
                "Sales by Status",
                "Current month sales distribution by order status",
                "pie-chart",
                htmlContent);
        }

        private async Task<(string title, string description, string icon, string htmlContent)> BuildPayablesOverviewContentAsync(Enterprise enterprise)
        {
            var htmlContent = await _payableBillReportGeneratorService.GeneratePayablesOverviewReportAsync(enterprise);
            if (string.IsNullOrWhiteSpace(htmlContent))
                throw new InsertDatabaseException("No payable bills found in the current month to generate this report.");

            return (
                "Payables Overview",
                "Current month payable exposure grouped by type and due status",
                "credit-card",
                htmlContent);
        }

        private async Task<List<Order>> GetCurrentMonthOrdersOrThrowAsync(Enterprise enterprise)
        {
            IEnumerable<Order> orders;
            try
            {
                orders = await _orderRepository.GetAllAsyncWithOrderedProductsByEnterprise(enterprise);
            }
            catch (NotFoundDatabaseException)
            {
                throw new InsertDatabaseException("No orders found in the current month to generate this report.");
            }

            var orderList = orders?.ToList() ?? new List<Order>();
            if (orderList.Count == 0)
                throw new InsertDatabaseException("No orders found in the current month to generate this report.");

            return orderList;
        }

        private async Task<List<ReportListItemDTO>> GetListAsync(string key)
        {
            var reportList = await GetCacheValueAsync<List<ReportListItemDTO>>(key);
            return reportList ?? new List<ReportListItemDTO>();
        }

        private async Task<T?> GetCacheValueAsync<T>(string key)
        {
            var cachedValue = await _cache.GetStringAsync(key);
            if (string.IsNullOrWhiteSpace(cachedValue))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(cachedValue, SerializerOptions);
            }
            catch
            {
                return default;
            }
        }

        private Task SetCacheValueAsync<T>(string key, T value)
        {
            var cacheValue = JsonSerializer.Serialize(value, SerializerOptions);
            return _cache.SetStringAsync(
                key,
                cacheValue,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ReportsTtl,
                });
        }

        private static void EnsureContextIds(Guid enterpriseId, Guid userId)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Enterprise context is required.");

            if (userId == Guid.Empty)
                throw new InsertDatabaseException("User context is required.");
        }

        private static string GetUserListKey(Guid enterpriseId, Guid userId)
        {
            return $"reports:list:{enterpriseId:N}:{userId:N}";
        }

        private static string GetReportItemKey(Guid enterpriseId, Guid userId, Guid reportId)
        {
            return $"reports:item:{enterpriseId:N}:{userId:N}:{reportId:N}";
        }
    }
}
