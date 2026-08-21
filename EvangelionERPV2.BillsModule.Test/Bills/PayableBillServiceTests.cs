using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
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
                BillType = 3,
                Amount = 999,
                Items =
                [
                    new PayableBillProduct
                    {
                        ProductId = product.Id,
                        Quantity = 2,
                        UnitValue = 12.5m,
                        UnitOfMeasure = "Box"
                    }
                ]
            };

            var result = await service.CreateAsync(entity);

            Assert.Equal(25, result.Amount);
            Assert.Equal(3, result.BillType);
            Assert.Single(payableItems);
            Assert.Equal("Unit", payableItems[0].UnitOfMeasure);
            productRepo.Verify(x => x.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>()), Times.AtLeastOnce);
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
        public async Task CreateAsync_SamePayloadTwice_CreatesTwoIndependentBills()
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
            var now = DateTime.UtcNow;

            PayableBill BuildBill()
            {
                return new PayableBill
                {
                    Description = "Office supply",
                    EnterpriseId = enterpriseId,
                    IsPaid = true,
                    PaidAt = now,
                    BillType = 3,
                    DueDate = now.AddDays(30),
                    Amount = 999,
                    Items =
                    [
                        new PayableBillProduct
                        {
                            ProductId = product.Id,
                            Quantity = 2,
                            UnitValue = 12.5m,
                            UnitOfMeasure = "Box"
                        }
                    ]
                };
            }

            var (payableBillRepo, _, _, _, _, service, _) = BuildSut(products: [product]);
            payableBillRepo
                .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var firstResult = await service.CreateAsync(BuildBill());
            var secondResult = await service.CreateAsync(BuildBill());

            Assert.NotEqual(firstResult.Id, secondResult.Id);
            payableBillRepo.Verify(x => x.CreateAsync(It.IsAny<PayableBill>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CreateAsync_WithInvalidBillType_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var (_, _, _, _, _, service, _) = BuildSut();

            var entity = new PayableBill
            {
                Description = "Invalid type",
                Amount = 150,
                EnterpriseId = enterpriseId,
                BillType = 999
            };

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.CreateAsync(entity));
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
        public async Task RefundAsync_WithReceivedProducts_ShouldRollbackStock_AndLockBill()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Notebook",
                StorageQuantity = 10,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Supplier return",
                IsActive = true,
                ProductsReceivedAt = DateTime.UtcNow,
                Amount = 500
            };

            var item = new PayableBillProduct
            {
                Id = Guid.NewGuid(),
                PayableBillId = bill.Id,
                ProductId = product.Id,
                Quantity = 5,
                UnitValue = 100,
                LineAmount = 500,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var (_, _, _, _, _, service, itemsStore) = BuildSut(
                payableBills: [bill],
                payableItems: [item],
                products: [product]);

            var refunded = await service.RefundAsync(bill.Id, enterpriseId, "Supplier shipped wrong model");

            Assert.NotNull(refunded.RefundedAt);
            Assert.Equal("Supplier shipped wrong model", refunded.RefundReason);
            Assert.Equal(0, refunded.Amount);
            Assert.False(refunded.IsPaid);
            Assert.Equal(5, product.StorageQuantity);
            Assert.All(itemsStore, line =>
            {
                Assert.Equal(0, line.Quantity);
                Assert.Equal(0, line.UnitValue);
                Assert.Equal(0, line.LineAmount);
            });

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.MarkProductsReceivedAsync(bill.Id, enterpriseId));
        }

        [Fact]
        public async Task RefundAsync_WithoutItems_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "No items",
                IsActive = true
            };

            var (_, _, _, _, _, service, _) = BuildSut(payableBills: [bill]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.RefundAsync(bill.Id, enterpriseId, "No products to refund"));
        }

        [Fact]
        public async Task UpdateAsync_AfterRefund_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Locked",
                IsActive = true,
                RefundedAt = DateTime.UtcNow,
                RefundReason = "Already refunded"
            };

            var (_, _, _, _, _, service, _) = BuildSut(payableBills: [bill]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.UpdateAsync(new PayableBill
                {
                    Id = bill.Id,
                    Description = bill.Description,
                    DueDate = DateTime.UtcNow,
                    Amount = 10,
                    IsPaid = false
                }, enterpriseId));
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
        public async Task DeleteAsync_AfterProductsReceived_ShouldRollbackStockAndSoftDelete()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Cable",
                StorageQuantity = 10,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Delete with rollback",
                IsActive = true,
                ProductsReceivedAt = DateTime.UtcNow
            };

            var item = new PayableBillProduct
            {
                Id = Guid.NewGuid(),
                PayableBillId = bill.Id,
                ProductId = product.Id,
                Quantity = 3,
                UnitValue = 10,
                LineAmount = 30,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var (_, _, _, _, _, service, payableItemsStore) = BuildSut(
                payableBills: [bill],
                payableItems: [item],
                products: [product]);

            var deleted = await service.DeleteAsync(bill.Id, enterpriseId);

            Assert.False(deleted.IsActive ?? true);
            Assert.Equal(7, product.StorageQuantity);
            Assert.All(payableItemsStore, stored => Assert.False(stored.IsActive ?? true));
        }

        [Fact]
        public async Task DeleteAsync_AfterRefund_ShouldSoftDelete()
        {
            var enterpriseId = Guid.NewGuid();
            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Cannot delete refunded",
                IsActive = true,
                RefundedAt = DateTime.UtcNow,
                RefundReason = "Supplier return"
            };

            var (_, _, _, _, _, service, _) = BuildSut(payableBills: [bill]);

            var deleted = await service.DeleteAsync(bill.Id, enterpriseId);
            Assert.False(deleted.IsActive ?? true);
        }

        [Fact]
        public async Task GetByEnterpriseIdAsync_ShouldFilterByIsActiveAndBillType()
        {
            var enterpriseId = Guid.NewGuid();
            var activeAccounts = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Active Accounts",
                BillType = 0,
                IsActive = true
            };

            var inactiveAccounts = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Inactive Accounts",
                BillType = 0,
                IsActive = false
            };

            var activeTaxes = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Active Taxes",
                BillType = 4,
                IsActive = true
            };

            var (_, _, _, _, _, service, _) = BuildSut(
                payableBills: [activeAccounts, inactiveAccounts, activeTaxes]);

            var activeOnly = (await service.GetByEnterpriseIdAsync(enterpriseId, isActive: true)).ToList();
            Assert.Equal(2, activeOnly.Count);
            Assert.DoesNotContain(activeOnly, x => x.Id == inactiveAccounts.Id);

            var inactiveOnly = (await service.GetByEnterpriseIdAsync(enterpriseId, isActive: false)).ToList();
            Assert.Single(inactiveOnly);
            Assert.Equal(inactiveAccounts.Id, inactiveOnly[0].Id);

            var allStatuses = (await service.GetByEnterpriseIdAsync(enterpriseId, isActive: null, billType: 0)).ToList();
            Assert.Equal(2, allStatuses.Count);
            Assert.All(allStatuses, bill => Assert.Equal(0, bill.BillType));
        }

        [Fact]
        public async Task GetByEnterpriseIdAsync_WithPeriod_ShouldReturnMonthBillsAndUnpaidCarryover()
        {
            var enterpriseId = Guid.NewGuid();
            var dueInJuly = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "July rent",
                DueDate = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            };

            var unpaidFromJune = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "June power, still unpaid",
                DueDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                IsPaid = false,
                IsActive = true
            };

            var paidInJune = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "June rent, paid",
                DueDate = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
                IsPaid = true,
                PaidAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            };

            var dueInAugust = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "August rent",
                DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            };

            var (_, _, _, _, _, service, _) = BuildSut(
                payableBills: [dueInJuly, unpaidFromJune, paidInJune, dueInAugust]);

            var julyView = (await service.GetByEnterpriseIdAsync(enterpriseId, year: 2026, month: 7)).ToList();

            Assert.Equal(2, julyView.Count);
            Assert.Contains(julyView, x => x.Id == dueInJuly.Id);
            Assert.Contains(julyView, x => x.Id == unpaidFromJune.Id);

            var juneView = (await service.GetByEnterpriseIdAsync(enterpriseId, year: 2026, month: 6)).ToList();
            Assert.Equal(2, juneView.Count);
            Assert.DoesNotContain(juneView, x => x.Id == dueInAugust.Id);
        }

        [Fact]
        public async Task GetByEnterpriseIdAsync_WithPeriod_ShouldBoundCarryoverToTwelveMonths()
        {
            var enterpriseId = Guid.NewGuid();
            var withinWindow = BuildPeriodBill(enterpriseId, "Unpaid 11 months ago", new DateTime(2025, 8, 10, 0, 0, 0, DateTimeKind.Utc));
            var onWindowEdge = BuildPeriodBill(enterpriseId, "Unpaid 12 months ago", new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            var outsideWindow = BuildPeriodBill(enterpriseId, "Unpaid 13 months ago", new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc));

            var (_, _, _, _, _, service, _) = BuildSut(
                payableBills: [withinWindow, onWindowEdge, outsideWindow]);

            var julyView = (await service.GetByEnterpriseIdAsync(enterpriseId, year: 2026, month: 7)).ToList();

            Assert.Equal(2, julyView.Count);
            Assert.Contains(julyView, x => x.Id == withinWindow.Id);
            Assert.Contains(julyView, x => x.Id == onWindowEdge.Id);
            Assert.DoesNotContain(julyView, x => x.Id == outsideWindow.Id);
        }

        [Fact]
        public async Task GetByEnterpriseIdAsync_ShouldOrderByDueDateSoTheFirstPageIsTheOldestBills()
        {
            var enterpriseId = Guid.NewGuid();
            var last = BuildPeriodBill(enterpriseId, "Due last", new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));
            var first = BuildPeriodBill(enterpriseId, "Due first", new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));
            var middle = BuildPeriodBill(enterpriseId, "Due in between", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));

            var (_, _, _, _, _, service, _) = BuildSut(payableBills: [last, first, middle]);

            var page = (await service.GetByEnterpriseIdAsync(enterpriseId, pageNumber: 1, pageSize: 2, year: 2026, month: 7)).ToList();

            Assert.Equal([first.Id, middle.Id], page.Select(x => x.Id).ToArray());
        }

        [Fact]
        public async Task GetByEnterpriseIdAsync_WithoutPayrollAccess_ShouldFilterPayrollBillsBeforePaging()
        {
            var enterpriseId = Guid.NewGuid();
            var payrollBills = Enumerable.Range(1, 3)
                .Select(day => new PayableBill
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Description = $"Folha {day}",
                    BillType = (int)EnumPayableBillType.Payroll,
                    DueDate = new DateTime(2026, 7, day, 0, 0, 0, DateTimeKind.Utc),
                    IsActive = true
                })
                .ToList();

            var rent = BuildPeriodBill(enterpriseId, "July rent", new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));

            var (_, _, _, _, _, service, _) = BuildSut(payableBills: [.. payrollBills, rent]);

            // Paging a month where payroll bills fill the whole first page must still return the
            // non-payroll bills, and must not leak the payroll count as a short page.
            var page = (await service.GetByEnterpriseIdAsync(
                enterpriseId,
                pageNumber: 1,
                pageSize: 3,
                year: 2026,
                month: 7,
                includePayroll: false)).ToList();

            var bill = Assert.Single(page);
            Assert.Equal(rent.Id, bill.Id);
        }

        private static StockMovement BuildSaleMovement(Guid enterpriseId, Guid productId, DateTime soldAt, decimal quantity)
        {
            return new StockMovement
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                ProductId = productId,
                // Sales are stored negative — the ledger records the direction, not just the size.
                Quantity = -quantity,
                Type = (int)EnumStockMovementType.Sale,
                CreatedAt = soldAt,
                IsActive = true
            };
        }

        private static PayableBill BuildPeriodBill(Guid enterpriseId, string description, DateTime dueDate)
        {
            return new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = description,
                DueDate = dueDate,
                IsPaid = false,
                IsActive = true
            };
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

            // Demand is read off the stock ledger now, so the fixture states it directly: four
            // sale dates ten days apart, which is what makes lead time 10 and coverage 10..20.
            var saleDates = new[] { now.AddDays(-40), now.AddDays(-30), now.AddDays(-20), now.AddDays(-10) };

            var stockMovements = new List<StockMovement>();
            foreach (var saleDate in saleDates)
            {
                stockMovements.Add(BuildSaleMovement(enterpriseId, productHigh.Id, saleDate, 10));
                stockMovements.Add(BuildSaleMovement(enterpriseId, productMedium.Id, saleDate, 10));
                stockMovements.Add(BuildSaleMovement(enterpriseId, productLow.Id, saleDate, 10));
            }

            // Insufficient history: only 3 sale dates => 2 intervals
            foreach (var saleDate in saleDates.Take(3))
                stockMovements.Add(BuildSaleMovement(enterpriseId, productNoHistory.Id, saleDate, 2));

            var (_, _, _, _, _, service, _) = BuildSut(
                stockMovements: stockMovements,
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

        [Fact]
        public async Task MarkProductsReceived_ShouldWriteReceiptMovement_PerItem()
        {
            var enterpriseId = Guid.NewGuid();
            var cable = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Cable",
                StorageQuantity = 5,
                UnitOfMeasure = "Unit",
                IsActive = true
            };
            var mouse = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Mouse",
                StorageQuantity = 0,
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

            var items = new List<PayableBillProduct>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = bill.Id,
                    ProductId = cable.Id,
                    Quantity = 7,
                    UnitValue = 10,
                    LineAmount = 70,
                    UnitOfMeasure = cable.UnitOfMeasure,
                    IsActive = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = bill.Id,
                    ProductId = mouse.Id,
                    Quantity = 3,
                    UnitValue = 20,
                    LineAmount = 60,
                    UnitOfMeasure = mouse.UnitOfMeasure,
                    IsActive = true
                }
            };

            var (_, _, _, _, movements, service, _) = BuildSut(
                payableBills: [bill],
                payableItems: items,
                products: [cable, mouse]);

            await service.MarkProductsReceivedAsync(bill.Id, enterpriseId);

            Assert.Equal(2, movements.Count);
            Assert.All(movements, movement =>
            {
                Assert.Equal((int)EnumStockMovementType.Receipt, movement.Type);
                Assert.Equal(bill.Id, movement.ReferenceId);
                Assert.Equal(enterpriseId, movement.EnterpriseId);
                Assert.True(movement.Quantity > 0);
            });

            var cableMovement = movements.Single(x => x.ProductId == cable.Id);
            Assert.Equal(7, cableMovement.Quantity);
            Assert.Equal(12, cableMovement.BalanceAfter);
        }

        [Fact]
        public async Task RefundAsync_WithReceivedProducts_ShouldWriteAdjustmentMovement_NotNegativeReceipt()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Keyboard",
                StorageQuantity = 4,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Wrong model",
                Amount = 100,
                ProductsReceivedAt = DateTime.UtcNow.AddDays(-1),
                IsActive = true
            };

            var item = new PayableBillProduct
            {
                Id = Guid.NewGuid(),
                PayableBillId = bill.Id,
                ProductId = product.Id,
                Quantity = 4,
                UnitValue = 25,
                LineAmount = 100,
                UnitOfMeasure = product.UnitOfMeasure,
                IsActive = true
            };

            var (_, _, _, _, movements, service, _) = BuildSut(
                payableBills: [bill],
                payableItems: [item],
                products: [product]);

            await service.RefundAsync(bill.Id, enterpriseId, "Supplier shipped wrong model");

            var movement = Assert.Single(movements);
            Assert.Equal((int)EnumStockMovementType.Adjustment, movement.Type);
            Assert.Equal(-4, movement.Quantity);
            Assert.Equal(0, movement.BalanceAfter);
            Assert.Equal(bill.Id, movement.ReferenceId);
            Assert.Equal(0, product.StorageQuantity);
        }

        /// <summary>
        /// M14 behaviour change: the reversal used to clamp at zero and carry on. The shared
        /// ledger entry point refuses instead, because a balance that cannot go below zero is the
        /// invariant the whole ledger rests on.
        /// </summary>
        [Fact]
        public async Task RefundAsync_WhenReceivedStockAlreadyLeft_ShouldThrowInsteadOfClampingToZero()
        {
            var enterpriseId = Guid.NewGuid();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Monitor",
                StorageQuantity = 1,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "Monitors",
                Amount = 900,
                ProductsReceivedAt = DateTime.UtcNow.AddDays(-2),
                IsActive = true
            };

            var item = new PayableBillProduct
            {
                Id = Guid.NewGuid(),
                PayableBillId = bill.Id,
                ProductId = product.Id,
                Quantity = 3,
                UnitValue = 300,
                LineAmount = 900,
                UnitOfMeasure = product.UnitOfMeasure,
                IsActive = true
            };

            var (_, _, _, _, movements, service, _) = BuildSut(
                payableBills: [bill],
                payableItems: [item],
                products: [product]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.RefundAsync(bill.Id, enterpriseId, "Returning to supplier"));

            Assert.Empty(movements);
            Assert.Equal(1, product.StorageQuantity);
        }

        private static (
            Mock<IRepository<PayableBill>> payableBillRepo,
            Mock<IRepository<PayableBillProduct>> payableBillProductRepo,
            Mock<IRepository<Product>> productRepo,
            Mock<IProductService<Product>> productService,
            List<StockMovement> movements,
            PayableBillService service,
            List<PayableBillProduct> payableItemsStore)
            BuildSut(
                List<PayableBill>? payableBills = null,
                List<PayableBillProduct>? payableItems = null,
                List<Product>? products = null,
                List<StockMovement>? stockMovements = null)
        {
            var payableBillStore = payableBills ?? [];
            var payableItemsStore = payableItems ?? [];
            var productStore = products ?? [];
            var stockMovementStore = stockMovements ?? [];

            var payableBillRepo = CreateRepositoryMock(payableBillStore);
            var payableBillProductRepo = CreateRepositoryMock(payableItemsStore);
            var productRepo = CreateRepositoryMock(productStore, throwsWhenEmpty: true);
            var enterpriseRepo = CreateRepositoryMock(new List<Enterprise>
            {
                new()
                {
                    Id = payableBillStore.FirstOrDefault()?.EnterpriseId
                         ?? productStore.FirstOrDefault()?.EnterpriseId
                         ?? Guid.NewGuid(),
                    Name = "Enterprise",
                    IsActive = true,
                    CurrentBalance = 0
                }
            });
            var stockMovementRepo = CreateRepositoryMock(stockMovementStore, throwsWhenEmpty: true);
            var productService = CreateStockLedgerMock(productStore, stockMovementStore);

            var service = new PayableBillService(
                payableBillRepo.Object,
                payableBillProductRepo.Object,
                productRepo.Object,
                enterpriseRepo.Object,
                stockMovementRepo.Object,
                productService.Object);

            return (payableBillRepo, payableBillProductRepo, productRepo, productService, stockMovementStore, service, payableItemsStore);
        }

        private static Mock<IProductService<Product>> CreateStockLedgerMock(
            List<Product> products,
            List<StockMovement> movements)
        {
            var service = new Mock<IProductService<Product>>();

            service
                .Setup(x => x.ApplyStockMovementAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                    It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>()))
                .ReturnsAsync((Guid productId, Guid enterpriseId, decimal delta, EnumStockMovementType type, Guid? referenceId) =>
                {
                    var product = products.FirstOrDefault(x =>
                        x.Id == productId && x.EnterpriseId == enterpriseId && x.IsActive == true);

                    // Mirrors the real compare-and-swap: no row, or a delta that would take the
                    // balance below zero, and nothing moves.
                    if (product == null || product.StorageQuantity + delta < 0)
                        throw new InsertDatabaseException(delta < 0
                            ? $"Insufficient stock for product [{productId}]."
                            : $"Product [{productId}] was not found.");

                    product.StorageQuantity += delta;
                    product.UpdatedAt = DateTime.UtcNow;

                    var movement = new StockMovement
                    {
                        Id = Guid.NewGuid(),
                        ProductId = productId,
                        EnterpriseId = enterpriseId,
                        Quantity = delta,
                        BalanceAfter = product.StorageQuantity,
                        Type = (int)type,
                        ReferenceId = referenceId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    };

                    movements.Add(movement);
                    return movement;
                });

            return service;
        }

        private static Mock<IRepository<TEntity>> CreateRepositoryMock<TEntity>(List<TEntity> records, bool throwsWhenEmpty = false)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => records.FirstOrDefault(entity => entity.Id == id)!);

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) =>
                {
                    IEnumerable<TEntity> query = records;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());

                    var result = query.ToList();
                    if (throwsWhenEmpty && result.Count == 0)
                        throw new NotFoundDatabaseException();

                    return result;
                });

            repo.Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((int? pageNumber, int? pageSize, Expression<Func<TEntity, bool>>? predicate) =>
                {
                    IEnumerable<TEntity> query = records;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());

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

            repo.Setup(x => x.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<TEntity, bool>>?>(),
                    It.IsAny<Expression<Func<TEntity, object>>?>()))
                .ReturnsAsync((
                    bool descending,
                    int? pageNumber,
                    int? pageSize,
                    Expression<Func<TEntity, bool>>? predicate,
                    Expression<Func<TEntity, object>>? orderBy) =>
                {
                    IEnumerable<TEntity> query = records;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());

                    if (orderBy != null)
                    {
                        var keySelector = orderBy.Compile();
                        query = descending
                            ? query.OrderByDescending(keySelector)
                            : query.OrderBy(keySelector);
                    }

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

            repo.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());

            return repo;
        }
    }
}
