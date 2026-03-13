using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class PayableBillServiceTests
    {
        [Fact]
        public async Task CreateAsync_WithItems_ShouldComputeAmountAndLockUomFromProduct()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Keyboard",
                UnitOfMeasure = "Unit",
                DefaultValue = 25,
                IsActive = true
            };

            var (payableBillRepo, payableBillProductRepo, productRepo, _, _, service, payableItems) = BuildSut(
                products: [product]);

            var entity = new PayableBill
            {
                Description = "Office supply",
                EnterpriseId = enterpriseId,
                IsPaid = false,
                Amount = 999,
                Items =
                [
                    new PayableBillProduct
                    {
                        ProductId = product.Id,
                        Quantity = 2,
                        UnitValue = 12.5,
                        UnitOfMeasure = "Box"
                    }
                ]
            };

            var result = await service.CreateAsync(entity);

            Assert.Equal(25, result.Amount);
            Assert.Single(payableItems);
            Assert.Equal("Unit", payableItems[0].UnitOfMeasure);
            productRepo.Verify(x => x.GetAllAsync(It.IsAny<Func<Product, bool>?>()), Times.AtLeastOnce);
            payableBillRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            payableBillProductRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_WithoutItems_ShouldKeepManualAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var (payableBillRepo, payableBillProductRepo, _, _, _, service, payableItems) = BuildSut();

            var entity = new PayableBill
            {
                Description = "Rent",
                Amount = 1500,
                EnterpriseId = enterpriseId
            };

            var result = await service.CreateAsync(entity);

            Assert.Equal(1500, result.Amount);
            Assert.Empty(payableItems);
            payableBillRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            payableBillProductRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_WithEmptyItemsPayload_ShouldKeepManualAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var (_, _, _, _, _, service, payableItems) = BuildSut();

            var entity = new PayableBill
            {
                Description = "Manual payable",
                Amount = 1400,
                EnterpriseId = enterpriseId,
                Items = []
            };

            var result = await service.CreateAsync(entity);

            Assert.Equal(1400, result.Amount);
            Assert.Empty(payableItems);
        }

        [Fact]
        public async Task MarkProductsReceived_ShouldIncreaseStockOnce_AndBlockSecondCall()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Cable",
                StorageQuantity = 5,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "IT purchase",
                IsActive = true
            };

            var item = new PayableBillProduct
            {
                Id = Guid.NewGuid(),
                PayableBillId = bill.Id,
                ProductId = product.Id,
                Quantity = 7,
                UnitValue = 10,
                LineAmount = 70,
                UnitOfMeasure = product.UnitOfMeasure,
                IsActive = true
            };

            var (payableRepo, _, _, _, _, service, _) = BuildSut(
                payableBills: [bill],
                payableItems: [item],
                products: [product]);

            var received = await service.MarkProductsReceivedAsync(bill.Id, enterpriseId);

            Assert.NotNull(received.ProductsReceivedAt);
            Assert.Equal(12, product.StorageQuantity);
            payableRepo.Verify(x => x.Update(It.IsAny<PayableBill>()), Times.AtLeastOnce);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.MarkProductsReceivedAsync(bill.Id, enterpriseId));
        }

        [Fact]
        public async Task UpdateAsync_AfterProductsReceived_WithItems_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Mouse",
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Accessories",
                IsActive = true,
                ProductsReceivedAt = DateTime.UtcNow
            };

            var (_, _, _, _, _, service, _) = BuildSut(
                payableBills: [bill],
                products: [product]);

            var updatePayload = new PayableBill
            {
                Id = bill.Id,
                EnterpriseId = enterpriseId,
                Description = bill.Description,
                DueDate = DateTime.UtcNow,
                Amount = 100,
                Items =
                [
                    new PayableBillProduct
                    {
                        ProductId = product.Id,
                        Quantity = 1,
                        UnitValue = 100
                    }
                ]
            };

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.UpdateAsync(updatePayload, enterpriseId));
        }

        [Fact]
        public async Task UpdateAsync_WithEmptyItemsPayload_ShouldKeepManualAmountAndClearItems()
        {
            var enterpriseId = Guid.NewGuid();
            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Manual update",
                Amount = 75,
                IsActive = true
            };

            var existingItem = new PayableBillProduct
            {
                Id = Guid.NewGuid(),
                PayableBillId = bill.Id,
                ProductId = Guid.NewGuid(),
                Quantity = 3,
                UnitValue = 25,
                LineAmount = 75,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var (_, _, _, _, _, service, payableItemsStore) = BuildSut(
                payableBills: [bill],
                payableItems: [existingItem]);

            var updated = await service.UpdateAsync(new PayableBill
            {
                Id = bill.Id,
                Description = "Manual update",
                DueDate = DateTime.UtcNow,
                PaidAt = null,
                IsPaid = false,
                Amount = 450,
                Items = []
            }, enterpriseId);

            Assert.Equal(450, updated.Amount);
            Assert.All(payableItemsStore, item => Assert.False(item.IsActive ?? false));
        }

        [Fact]
        public async Task DeleteAsync_AfterProductsReceived_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Cannot delete",
                IsActive = true,
                ProductsReceivedAt = DateTime.UtcNow
            };

            var (_, _, _, _, _, service, _) = BuildSut(payableBills: [bill]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.DeleteAsync(bill.Id, enterpriseId));
        }

        [Fact]
        public async Task GetReplenishmentSuggestions_ShouldInferCoverageAndSortByCriticality()
        {
            var enterpriseId = Guid.NewGuid();
            var now = DateTime.UtcNow.Date;
            var productHigh = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "High Risk",
                StorageQuantity = 2,
                IsActive = true
            };
            var productMedium = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Overstock",
                StorageQuantity = 60,
                IsActive = true
            };
            var productLow = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Healthy",
                StorageQuantity = 12,
                IsActive = true
            };
            var productNoHistory = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "No History",
                StorageQuantity = 0,
                IsActive = true
            };

            var orders = new List<Order>
            {
                new() { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CreatedAt = now.AddDays(-40), IsActive = true },
                new() { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CreatedAt = now.AddDays(-30), IsActive = true },
                new() { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CreatedAt = now.AddDays(-20), IsActive = true },
                new() { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CreatedAt = now.AddDays(-10), IsActive = true },
            };

            var orderedProducts = new List<OrderedProduct>();
            foreach (var order in orders)
            {
                orderedProducts.Add(new OrderedProduct { Id = Guid.NewGuid(), IsActive = true, OrderId = order.Id, ProductId = productHigh.Id, Quantity = 10, Value = 10 });
                orderedProducts.Add(new OrderedProduct { Id = Guid.NewGuid(), IsActive = true, OrderId = order.Id, ProductId = productMedium.Id, Quantity = 10, Value = 10 });
                orderedProducts.Add(new OrderedProduct { Id = Guid.NewGuid(), IsActive = true, OrderId = order.Id, ProductId = productLow.Id, Quantity = 10, Value = 10 });
            }

            // Insufficient history: only 3 orders => 2 intervals
            orderedProducts.Add(new OrderedProduct { Id = Guid.NewGuid(), IsActive = true, OrderId = orders[0].Id, ProductId = productNoHistory.Id, Quantity = 2, Value = 3 });
            orderedProducts.Add(new OrderedProduct { Id = Guid.NewGuid(), IsActive = true, OrderId = orders[1].Id, ProductId = productNoHistory.Id, Quantity = 2, Value = 3 });
            orderedProducts.Add(new OrderedProduct { Id = Guid.NewGuid(), IsActive = true, OrderId = orders[2].Id, ProductId = productNoHistory.Id, Quantity = 2, Value = 3 });

            var (_, _, _, _, _, service, _) = BuildSut(
                orders: orders,
                orderedProducts: orderedProducts,
                products: [productHigh, productMedium, productLow, productNoHistory]);

            var suggestions = (await service.GetReplenishmentSuggestionsAsync(
                enterpriseId,
                new ReplenishmentSuggestionRequestDTO
                {
                    HistoryWindowDays = 60,
                    PageNumber = 1,
                    PageSize = 10,
                    SortByCriticality = true
                })).ToList();

            Assert.Equal(3, suggestions.Count);
            Assert.Equal("high", suggestions[0].Criticality);
            Assert.Equal("stockout", suggestions[0].Alert);
            Assert.Equal("High Risk", suggestions[0].ProductName);
            Assert.True(suggestions[0].SuggestedQuantity > 0);
            Assert.Equal(10, suggestions[0].LeadTimeDays);
            Assert.Equal(10, suggestions[0].MinCoverageDays);
            Assert.Equal(20, suggestions[0].MaxCoverageDays);

            Assert.Equal("medium", suggestions[1].Criticality);
            Assert.Equal("overstock", suggestions[1].Alert);
            Assert.Equal("Overstock", suggestions[1].ProductName);

            Assert.Equal("low", suggestions[2].Criticality);
            Assert.Equal("none", suggestions[2].Alert);
            Assert.Equal("Healthy", suggestions[2].ProductName);

            Assert.DoesNotContain(suggestions, x => x.ProductId == productNoHistory.Id);
        }

        [Fact]
        public async Task GetReplenishmentSuggestions_WhenNoHistory_ShouldReturnEmpty()
        {
            var enterpriseId = Guid.NewGuid();
            var (_, _, _, _, _, service, _) = BuildSut();

            var suggestions = await service.GetReplenishmentSuggestionsAsync(
                enterpriseId,
                new ReplenishmentSuggestionRequestDTO
                {
                    HistoryWindowDays = 90,
                    PageNumber = 1,
                    PageSize = 50,
                    SortByCriticality = true
                });

            Assert.Empty(suggestions);
        }

        private static (
            Mock<IRepository<PayableBill>> payableBillRepo,
            Mock<IRepository<PayableBillProduct>> payableBillProductRepo,
            Mock<IRepository<Product>> productRepo,
            Mock<IRepository<Order>> orderRepo,
            Mock<IRepository<OrderedProduct>> orderedProductRepo,
            PayableBillService service,
            List<PayableBillProduct> payableItemsStore)
            BuildSut(
                List<PayableBill>? payableBills = null,
                List<PayableBillProduct>? payableItems = null,
                List<Product>? products = null,
                List<Order>? orders = null,
                List<OrderedProduct>? orderedProducts = null)
        {
            var payableBillStore = payableBills ?? [];
            var payableItemsStore = payableItems ?? [];
            var productStore = products ?? [];
            var orderStore = orders ?? [];
            var orderedProductStore = orderedProducts ?? [];

            var payableBillRepo = CreateRepositoryMock(payableBillStore);
            var payableBillProductRepo = CreateRepositoryMock(payableItemsStore);
            var productRepo = CreateRepositoryMock(productStore, throwsWhenEmpty: true);
            var orderRepo = CreateRepositoryMock(orderStore, throwsWhenEmpty: true);
            var orderedProductRepo = CreateRepositoryMock(orderedProductStore, throwsWhenEmpty: true);

            var service = new PayableBillService(
                payableBillRepo.Object,
                payableBillProductRepo.Object,
                productRepo.Object,
                orderRepo.Object,
                orderedProductRepo.Object);

            return (payableBillRepo, payableBillProductRepo, productRepo, orderRepo, orderedProductRepo, service, payableItemsStore);
        }

        private static Mock<IRepository<TEntity>> CreateRepositoryMock<TEntity>(List<TEntity> records, bool throwsWhenEmpty = false)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => records.FirstOrDefault(entity => entity.Id == id)!);

            repo.Setup(x => x.GetAllAsync(It.IsAny<Func<TEntity, bool>?>()))
                .ReturnsAsync((Func<TEntity, bool>? predicate) =>
                {
                    IEnumerable<TEntity> query = records;
                    if (predicate != null)
                        query = query.Where(predicate);

                    var result = query.ToList();
                    if (throwsWhenEmpty && result.Count == 0)
                        throw new NotFoundDatabaseException();

                    return result;
                });

            repo.Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<TEntity, bool>?>()))
                .ReturnsAsync((int? pageNumber, int? pageSize, Func<TEntity, bool>? predicate) =>
                {
                    IEnumerable<TEntity> query = records;
                    if (predicate != null)
                        query = query.Where(predicate);

                    if (pageNumber.HasValue && pageSize.HasValue && pageNumber.Value > 0 && pageSize.Value > 0)
                    {
                        query = query
                            .Skip((pageNumber.Value - 1) * pageSize.Value)
                            .Take(pageSize.Value);
                    }

                    var result = query.ToList();
                    if (throwsWhenEmpty && result.Count == 0)
                        throw new NotFoundDatabaseException();

                    return result;
                });

            repo.Setup(x => x.CreateAsync(It.IsAny<TEntity>()))
                .ReturnsAsync((TEntity entity) =>
                {
                    records.Add(entity);
                    return entity;
                });

            repo.Setup(x => x.CreateRangeAsync(It.IsAny<IEnumerable<TEntity>>()))
                .ReturnsAsync((IEnumerable<TEntity> entities) =>
                {
                    var list = entities.ToList();
                    records.AddRange(list);
                    return list;
                });

            repo.Setup(x => x.Update(It.IsAny<TEntity>()))
                .Returns((TEntity entity) =>
                {
                    var index = records.FindIndex(existing => existing.Id == entity.Id);
                    if (index >= 0)
                        records[index] = entity;
                    else
                        records.Add(entity);
                    return entity;
                });

            repo.Setup(x => x.UpdateRange(It.IsAny<IEnumerable<TEntity>>()))
                .Returns((IEnumerable<TEntity> entities) =>
                {
                    foreach (var entity in entities)
                    {
                        var index = records.FindIndex(existing => existing.Id == entity.Id);
                        if (index >= 0)
                            records[index] = entity;
                        else
                            records.Add(entity);
                    }
                    return entities;
                });

            repo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return repo;
        }
    }
}
