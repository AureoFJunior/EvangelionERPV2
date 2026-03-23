using System.Text;
using System.Text.Json;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ReportsModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace EvangelionERPV2.ReportsModule.Test.Reports
{
    public class ReportsServiceTests
    {
        [Fact]
        public async Task GenerateAsync_StoresItemAndListWithOneDayTtl()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, _, _, _, enterprise) = CreateService(cache);
            var userId = Guid.NewGuid();

            var created = await service.GenerateAsync(enterprise.Id, userId, EnumReportType.Stock);

            var listKey = $"reports:list:{enterprise.Id:N}:{userId:N}";
            var itemKey = $"reports:item:{enterprise.Id:N}:{userId:N}:{created.Id:N}";

            Assert.True(cache.Entries.ContainsKey(listKey));
            Assert.True(cache.Entries.ContainsKey(itemKey));
            Assert.Equal(TimeSpan.FromDays(1), cache.Entries[listKey].Options.AbsoluteExpirationRelativeToNow);
            Assert.Equal(TimeSpan.FromDays(1), cache.Entries[itemKey].Options.AbsoluteExpirationRelativeToNow);

            var cachedListJson = Encoding.UTF8.GetString(cache.Entries[listKey].Value);
            var cachedList = JsonSerializer.Deserialize<List<ReportListItemDTO>>(cachedListJson);
            Assert.NotNull(cachedList);
            Assert.Single(cachedList!);
            Assert.Equal(created.Id, cachedList[0].Id);
        }

        [Fact]
        public async Task GenerateAsync_CapsUserHistoryAtFifty()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, _, _, _, enterprise) = CreateService(cache);
            var userId = Guid.NewGuid();

            for (var i = 0; i < 55; i++)
                await service.GenerateAsync(enterprise.Id, userId, EnumReportType.Stock);

            var reports = (await service.GetUserReportsAsync(enterprise.Id, userId)).ToList();

            Assert.Equal(50, reports.Count);
            Assert.True(reports.Zip(reports.Skip(1), (current, next) => current.Date >= next.Date).All(x => x));
        }

        [Fact]
        public async Task GetByIdAsync_DoesNotLeakAcrossUsers()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, _, _, _, enterprise) = CreateService(cache);
            var ownerId = Guid.NewGuid();
            var anotherUserId = Guid.NewGuid();

            var created = await service.GenerateAsync(enterprise.Id, ownerId, EnumReportType.Stock);
            var fromAnotherUser = await service.GetByIdAsync(enterprise.Id, anotherUserId, created.Id);

            Assert.Null(fromAnotherUser);
        }

        [Fact]
        public async Task GenerateAsync_MonthlyBilling_UsesOrderGenerator()
        {
            var cache = new TrackingDistributedCache();
            var orderItems = new List<Order>
            {
                new Order
                {
                    Id = Guid.NewGuid(),
                    TotalValue = 150,
                    OrderedProduct = new List<OrderedProduct>(),
                }
            };

            var (service, _, _, orderGenerator, _, orderRepository, enterprise) = CreateService(cache, orders: orderItems);
            var userId = Guid.NewGuid();

            var created = await service.GenerateAsync(enterprise.Id, userId, EnumReportType.MonthlyBilling);

            Assert.Equal(EnumReportType.MonthlyBilling, created.Type);
            orderRepository.Verify(r => r.GetAllAsyncWithOrderedProductsByEnterprise(It.Is<Enterprise>(e => e.Id == enterprise.Id)), Times.Once);
            orderGenerator.Verify(g => g.GenerateMonthlyBillingReportAsync(
                It.Is<Enterprise>(e => e.Id == enterprise.Id),
                It.IsAny<IEnumerable<Order>>()), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_TopProductsRevenue_UsesOrderGenerator()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, orderGenerator, _, orderRepository, enterprise) = CreateService(cache);

            var created = await service.GenerateAsync(enterprise.Id, Guid.NewGuid(), EnumReportType.TopProductsRevenue);

            Assert.Equal(EnumReportType.TopProductsRevenue, created.Type);
            orderRepository.Verify(r => r.GetAllAsyncWithOrderedProductsByEnterprise(It.Is<Enterprise>(e => e.Id == enterprise.Id)), Times.Once);
            orderGenerator.Verify(g => g.GenerateTopProductsByRevenueReportAsync(
                It.Is<Enterprise>(e => e.Id == enterprise.Id),
                It.IsAny<IEnumerable<Order>>()), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_SalesByStatus_UsesOrderGenerator()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, orderGenerator, _, orderRepository, enterprise) = CreateService(cache);

            var created = await service.GenerateAsync(enterprise.Id, Guid.NewGuid(), EnumReportType.SalesByStatus);

            Assert.Equal(EnumReportType.SalesByStatus, created.Type);
            orderRepository.Verify(r => r.GetAllAsyncWithOrderedProductsByEnterprise(It.Is<Enterprise>(e => e.Id == enterprise.Id)), Times.Once);
            orderGenerator.Verify(g => g.GenerateSalesByStatusReportAsync(
                It.Is<Enterprise>(e => e.Id == enterprise.Id),
                It.IsAny<IEnumerable<Order>>()), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_PayablesOverview_UsesPayableGenerator()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, _, payableGenerator, _, enterprise) = CreateService(cache);

            var created = await service.GenerateAsync(enterprise.Id, Guid.NewGuid(), EnumReportType.PayablesOverview);

            Assert.Equal(EnumReportType.PayablesOverview, created.Type);
            payableGenerator.Verify(g => g.GeneratePayablesOverviewReportAsync(It.Is<Enterprise>(e => e.Id == enterprise.Id)), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WhenGeneratorReturnsEmpty_ThrowsControlledError()
        {
            var cache = new TrackingDistributedCache();
            var (service, _, _, _, payableGenerator, _, enterprise) = CreateService(cache, payablesHtml: string.Empty);

            await Assert.ThrowsAsync<InsertDatabaseException>(() =>
                service.GenerateAsync(enterprise.Id, Guid.NewGuid(), EnumReportType.PayablesOverview));

            payableGenerator.Verify(g => g.GeneratePayablesOverviewReportAsync(It.Is<Enterprise>(e => e.Id == enterprise.Id)), Times.Once);
        }

        private static (
            ReportsService service,
            Mock<IRepository<Enterprise>> enterpriseRepository,
            Mock<IProductReportGeneratorService> productReportGenerator,
            Mock<IOrderReportGeneratorService> orderReportGenerator,
            Mock<IPayableBillReportGeneratorService> payableBillReportGenerator,
            Mock<IOrderRepository<Order>> orderRepository,
            Enterprise enterprise) CreateService(
            IDistributedCache cache,
            string stockHtml = "<html><body>stock</body></html>",
            string monthlyHtml = "<html><body>monthly</body></html>",
            string topProductsHtml = "<html><body>top products</body></html>",
            string salesByStatusHtml = "<html><body>sales by status</body></html>",
            string payablesHtml = "<html><body>payables overview</body></html>",
            IEnumerable<Order>? orders = null)
        {
            var enterprise = new Enterprise
            {
                Id = Guid.NewGuid(),
                Name = "Nerv",
                IsActive = true,
                Currency = "USD",
                Email = "nerv@hq.com",
                Adress = "GeoFront",
                PhoneNumber = "+1 555 0000",
            };

            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            enterpriseRepository.Setup(r => r.GetByIdAsync(enterprise.Id)).ReturnsAsync(enterprise);

            var productReportGenerator = new Mock<IProductReportGeneratorService>();
            productReportGenerator.Setup(g => g.GenerateStockReportAsync(It.Is<Enterprise>(e => e.Id == enterprise.Id)))
                .ReturnsAsync(stockHtml);

            var orderReportGenerator = new Mock<IOrderReportGeneratorService>();
            orderReportGenerator.Setup(g => g.GenerateMonthlyBillingReportAsync(It.IsAny<Enterprise>(), It.IsAny<IEnumerable<Order>>()))
                .ReturnsAsync(monthlyHtml);
            orderReportGenerator.Setup(g => g.GenerateTopProductsByRevenueReportAsync(It.IsAny<Enterprise>(), It.IsAny<IEnumerable<Order>>()))
                .ReturnsAsync(topProductsHtml);
            orderReportGenerator.Setup(g => g.GenerateSalesByStatusReportAsync(It.IsAny<Enterprise>(), It.IsAny<IEnumerable<Order>>()))
                .ReturnsAsync(salesByStatusHtml);

            var payableBillReportGenerator = new Mock<IPayableBillReportGeneratorService>();
            payableBillReportGenerator
                .Setup(g => g.GeneratePayablesOverviewReportAsync(It.IsAny<Enterprise>()))
                .ReturnsAsync(payablesHtml);

            var orderRepository = new Mock<IOrderRepository<Order>>();
            orderRepository.Setup(r => r.GetAllAsyncWithOrderedProductsByEnterprise(It.Is<Enterprise>(e => e.Id == enterprise.Id)))
                .ReturnsAsync(orders ?? new List<Order>
                {
                    new Order
                    {
                        Id = Guid.NewGuid(),
                        TotalValue = 100,
                        OrderedProduct = new List<OrderedProduct>(),
                    }
                });

            var service = new ReportsService(
                cache,
                enterpriseRepository.Object,
                productReportGenerator.Object,
                orderReportGenerator.Object,
                payableBillReportGenerator.Object,
                orderRepository.Object);

            return (service, enterpriseRepository, productReportGenerator, orderReportGenerator, payableBillReportGenerator, orderRepository, enterprise);
        }

        private sealed class TrackingDistributedCache : IDistributedCache
        {
            public sealed record CacheEntry(byte[] Value, DistributedCacheEntryOptions Options);

            private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
            public IReadOnlyDictionary<string, CacheEntry> Entries => _entries;

            public byte[]? Get(string key)
            {
                return _entries.TryGetValue(key, out var entry) ? entry.Value : null;
            }

            public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            {
                return Task.FromResult(Get(key));
            }

            public void Refresh(string key)
            {
            }

            public Task RefreshAsync(string key, CancellationToken token = default)
            {
                return Task.CompletedTask;
            }

            public void Remove(string key)
            {
                _entries.Remove(key);
            }

            public Task RemoveAsync(string key, CancellationToken token = default)
            {
                Remove(key);
                return Task.CompletedTask;
            }

            public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            {
                _entries[key] = new CacheEntry(value, options);
            }

            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            {
                Set(key, value, options);
                return Task.CompletedTask;
            }
        }
    }
}
