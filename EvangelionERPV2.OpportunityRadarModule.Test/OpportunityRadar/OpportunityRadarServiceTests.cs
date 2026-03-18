using System.Text.Json;
using System.Linq.Expressions;
using EvangelionERPV2.OpportunityRadarModule.Application.Configs;
using EvangelionERPV2.OpportunityRadarModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.OpportunityRadarModule.Test
{
    public class OpportunityRadarServiceTests
    {
        [Fact]
        public async Task RecomputeAsync_ShouldGenerateCoreDetectors_AndSortByPriority()
        {
            var enterpriseId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var productA = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "A", DefaultValue = 120, StorageQuantity = 60, IsActive = true };
            var productB = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "B", DefaultValue = 100, StorageQuantity = 55, IsActive = true };
            var productC = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "C", DefaultValue = 80, StorageQuantity = 0.5, IsActive = true };
            var productD = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "D", DefaultValue = 70, StorageQuantity = 40, IsActive = true };

            var customerPremium = new Customer { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "Premium", Document = "12345678000199", Adress = "Sao Paulo, SP", IsActive = true };
            var customerValue = new Customer { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "Value", Document = "12345678901", Adress = "Rio de Janeiro, RJ", IsActive = true };

            var orders = new List<Order>
            {
                CreateOrder(enterpriseId, customerPremium.Id, now.AddDays(-40), 220, 2),
                CreateOrder(enterpriseId, customerPremium.Id, now.AddDays(-35), 220, 2),
                CreateOrder(enterpriseId, customerPremium.Id, now.AddDays(-30), 220, 3),
                CreateOrder(enterpriseId, customerValue.Id, now.AddDays(-25), 120, 2),
                CreateOrder(enterpriseId, customerValue.Id, now.AddDays(-20), 120, 3),
                CreateOrder(enterpriseId, customerValue.Id, now.AddDays(-15), 120, 4),
                CreateOrder(enterpriseId, customerValue.Id, now.AddDays(-5), 100, 0),
                CreateOrder(enterpriseId, customerValue.Id, now.AddDays(-2), 100, 1)
            };

            var orderLines = new List<OrderedProduct>
            {
                CreateOrderLine(orders[0].Id, productA.Id, 1, 120),
                CreateOrderLine(orders[0].Id, productB.Id, 1, 100),
                CreateOrderLine(orders[1].Id, productA.Id, 1, 120),
                CreateOrderLine(orders[1].Id, productB.Id, 1, 100),
                CreateOrderLine(orders[2].Id, productA.Id, 1, 120),
                CreateOrderLine(orders[2].Id, productB.Id, 1, 100),
                CreateOrderLine(orders[3].Id, productC.Id, 1, 80),
                CreateOrderLine(orders[3].Id, productD.Id, 1, 40),
                CreateOrderLine(orders[4].Id, productC.Id, 1, 80),
                CreateOrderLine(orders[4].Id, productD.Id, 1, 40),
                CreateOrderLine(orders[5].Id, productC.Id, 1, 80),
                CreateOrderLine(orders[5].Id, productD.Id, 1, 40),
                // open demand on product C
                CreateOrderLine(orders[6].Id, productC.Id, 4, 80),
                CreateOrderLine(orders[7].Id, productC.Id, 3, 80)
            };

            var payableBills = BuildSupplierPayables(enterpriseId, now);
            var payableItems = BuildSupplierPayableItems(payableBills, productA, productB, productC, productD);

            var sut = BuildSut(
                enterprises: [new Enterprise { Id = enterpriseId, Name = "EVA", IsActive = true }],
                products: [productA, productB, productC, productD],
                customers: [customerPremium, customerValue],
                orders: orders,
                orderedProducts: orderLines,
                payableBills: payableBills,
                payableItems: payableItems);

            var run = await sut.Service.RecomputeAsync(
                enterpriseId,
                Guid.NewGuid(),
                new OpportunityRecomputeRequestDTO { HistoryWindowDays = 90 },
                "manual");

            Assert.Equal("completed", run.Status);
            Assert.True(run.TotalGenerated > 0);

            var page = await sut.Service.GetOpportunitiesAsync(enterpriseId, new OpportunityFilterDTO { Page = 1, PageSize = 50 });
            var items = page.Items.ToList();

            Assert.Contains(items, x => x.Type == "cross_sell");
            Assert.Contains(items, x => x.Type == "supplier_hidden_cost");
            Assert.Contains(items, x => x.Type == "commercial_reallocation");

            Assert.True(items.Count > 1);
            for (var i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].PriorityScore >= items[i].PriorityScore);
            }

            foreach (var item in items)
            {
                Assert.InRange(item.ConfidenceScore, 0, 100);
                Assert.False(string.IsNullOrWhiteSpace(item.Recommendation?.ActionPayloadJson));
                _ = JsonDocument.Parse(item.Recommendation!.ActionPayloadJson);
            }
        }

        [Fact]
        public async Task RecomputeAsync_ShouldGenerateBundleAndSuppressedDemand_WhenDataSupportsRules()
        {
            var enterpriseId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var productA = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "A", DefaultValue = 120, StorageQuantity = 70, IsActive = true };
            var productB = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "B", DefaultValue = 60, StorageQuantity = 65, IsActive = true };
            var productC = new Product { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Name = "C", DefaultValue = 80, StorageQuantity = 0.5, IsActive = true };

            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Customer",
                Document = "12345678000199",
                Adress = "Sao Paulo, SP",
                IsActive = true
            };

            var orders = new List<Order>
            {
                CreateOrder(enterpriseId, customer.Id, now.AddDays(-30), 260, 2),
                CreateOrder(enterpriseId, customer.Id, now.AddDays(-20), 260, 3),
                CreateOrder(enterpriseId, customer.Id, now.AddDays(-10), 260, 4),
                CreateOrder(enterpriseId, customer.Id, now.AddDays(-3), 120, 0),
                CreateOrder(enterpriseId, customer.Id, now.AddDays(-1), 120, 1)
            };

            var orderLines = new List<OrderedProduct>
            {
                CreateOrderLine(orders[0].Id, productA.Id, 1, 120),
                CreateOrderLine(orders[0].Id, productB.Id, 1, 60),
                CreateOrderLine(orders[0].Id, productC.Id, 10, 80),

                CreateOrderLine(orders[1].Id, productA.Id, 1, 120),
                CreateOrderLine(orders[1].Id, productB.Id, 1, 60),
                CreateOrderLine(orders[1].Id, productC.Id, 10, 80),

                CreateOrderLine(orders[2].Id, productA.Id, 1, 120),
                CreateOrderLine(orders[2].Id, productB.Id, 1, 60),
                CreateOrderLine(orders[2].Id, productC.Id, 10, 80),

                // Open demand pressure on product C
                CreateOrderLine(orders[3].Id, productC.Id, 30, 80),
                CreateOrderLine(orders[4].Id, productC.Id, 15, 80)
            };

            var payableBills = new List<PayableBill>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Description = "alpha - main",
                    Amount = 750,
                    DueDate = now.AddDays(-28),
                    ProductsReceivedAt = now.AddDays(-27),
                    IsActive = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Description = "alpha - main",
                    Amount = 750,
                    DueDate = now.AddDays(-18),
                    ProductsReceivedAt = now.AddDays(-17),
                    IsActive = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Description = "alpha - main",
                    Amount = 750,
                    DueDate = now.AddDays(-8),
                    ProductsReceivedAt = now.AddDays(-7),
                    IsActive = true
                }
            };

            var payableItems = new List<PayableBillProduct>();
            foreach (var bill in payableBills)
            {
                payableItems.Add(new PayableBillProduct
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = bill.Id,
                    ProductId = productA.Id,
                    Quantity = 10,
                    UnitValue = 20,
                    LineAmount = 200,
                    UnitOfMeasure = "Unit",
                    IsActive = true,
                    CreatedAt = bill.CreatedAt
                });

                payableItems.Add(new PayableBillProduct
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = bill.Id,
                    ProductId = productB.Id,
                    Quantity = 10,
                    UnitValue = 55,
                    LineAmount = 550,
                    UnitOfMeasure = "Unit",
                    IsActive = true,
                    CreatedAt = bill.CreatedAt
                });
            }

            var sut = BuildSut(
                enterprises: [new Enterprise { Id = enterpriseId, Name = "EVA", IsActive = true }],
                products: [productA, productB, productC],
                customers: [customer],
                orders: orders,
                orderedProducts: orderLines,
                payableBills: payableBills,
                payableItems: payableItems);

            await sut.Service.RecomputeAsync(
                enterpriseId,
                Guid.NewGuid(),
                new OpportunityRecomputeRequestDTO { HistoryWindowDays = 90 },
                "manual");

            var page = await sut.Service.GetOpportunitiesAsync(enterpriseId, new OpportunityFilterDTO { Page = 1, PageSize = 50 });
            var items = page.Items.ToList();

            Assert.Contains(items, x => x.Type == "bundle_margin");
            Assert.Contains(items, x => x.Type == "suppressed_demand");
            Assert.Contains(items, x => x.Type == "low_stock");
        }

        [Fact]
        public async Task AddFeedback_ShouldBlockAccepted_WhenUserIsNotManager()
        {
            var enterpriseId = Guid.NewGuid();
            var sut = BuildSut(enterprises: [new Enterprise { Id = enterpriseId, Name = "EVA", IsActive = true }]);

            var opportunity = new Opportunity
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Type = "cross_sell",
                Status = "new",
                Title = "Any",
                Description = "Any",
                SourceRule = "rule",
                SourceModel = "model",
                Fingerprint = "f-1",
                RunId = Guid.NewGuid(),
                IsActive = true
            };

            sut.OpportunityStore.Add(opportunity);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => sut.Service.AddFeedbackAsync(
                enterpriseId,
                opportunity.Id,
                Guid.NewGuid(),
                new OpportunityFeedbackRequestDTO { Status = "accepted" },
                canApproveExecution: false));
        }

        [Fact]
        public async Task AddFeedback_ShouldMoveOpportunityToInAnalysis_WhenScheduled()
        {
            var enterpriseId = Guid.NewGuid();
            var sut = BuildSut(enterprises: [new Enterprise { Id = enterpriseId, Name = "EVA", IsActive = true }]);

            var opportunity = new Opportunity
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Type = "cross_sell",
                Status = "new",
                Title = "Any",
                Description = "Any",
                SourceRule = "rule",
                SourceModel = "model",
                Fingerprint = "f-in-analysis",
                RunId = Guid.NewGuid(),
                IsActive = true
            };

            sut.OpportunityStore.Add(opportunity);

            var feedback = await sut.Service.AddFeedbackAsync(
                enterpriseId,
                opportunity.Id,
                Guid.NewGuid(),
                new OpportunityFeedbackRequestDTO
                {
                    Status = "in_analysis",
                    Comment = "Scheduled for next sprint."
                },
                canApproveExecution: false);

            Assert.Equal("in_analysis", feedback.Status);
            Assert.Equal("in_analysis", opportunity.Status);
        }

        [Fact]
        public async Task AddFeedback_ShouldMoveOpportunityToExecuted_WhenImplemented()
        {
            var enterpriseId = Guid.NewGuid();
            var sut = BuildSut(enterprises: [new Enterprise { Id = enterpriseId, Name = "EVA", IsActive = true }]);

            var opportunity = new Opportunity
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Type = "cross_sell",
                Status = "accepted",
                Title = "Any",
                Description = "Any",
                SourceRule = "rule",
                SourceModel = "model",
                Fingerprint = "f-2",
                RunId = Guid.NewGuid(),
                IsActive = true
            };
            sut.OpportunityStore.Add(opportunity);

            var feedback = await sut.Service.AddFeedbackAsync(
                enterpriseId,
                opportunity.Id,
                Guid.NewGuid(),
                new OpportunityFeedbackRequestDTO
                {
                    Status = "implemented",
                    RealMarginImpact = 150
                },
                canApproveExecution: true);

            Assert.Equal("implemented", feedback.Status);
            Assert.Equal("executed", opportunity.Status);
        }

        [Fact]
        public async Task Summary_ShouldComputeAcceptanceImplementationAndUplift()
        {
            var enterpriseId = Guid.NewGuid();
            var sut = BuildSut(enterprises: [new Enterprise { Id = enterpriseId, Name = "EVA", IsActive = true }]);

            var executed = new Opportunity
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Type = "bundle_margin",
                Status = "executed",
                Title = "A",
                Description = "A",
                SourceRule = "rule",
                SourceModel = "model",
                Fingerprint = "f-3",
                EstimatedMarginImpact = 100,
                IsActive = true
            };
            var accepted = new Opportunity
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Type = "cross_sell",
                Status = "accepted",
                Title = "B",
                Description = "B",
                SourceRule = "rule",
                SourceModel = "model",
                Fingerprint = "f-4",
                EstimatedMarginImpact = 50,
                IsActive = true
            };
            sut.OpportunityStore.AddRange([executed, accepted]);

            sut.FeedbackStore.Add(new OpportunityFeedback
            {
                Id = Guid.NewGuid(),
                OpportunityId = executed.Id,
                Status = "implemented",
                RealMarginImpact = 120,
                IsActive = true
            });

            var summary = await sut.Service.GetSummaryAsync(enterpriseId);

            Assert.Equal(2, summary.Total);
            Assert.True(summary.AcceptanceRate > 0);
            Assert.True(summary.ImplementationRate > 0);
            Assert.True(summary.RealVsEstimatedUplift > 0);
        }

        private static List<PayableBill> BuildSupplierPayables(Guid enterpriseId, DateTime now)
        {
            var list = new List<PayableBill>();
            for (var i = 0; i < 3; i++)
            {
                list.Add(new PayableBill
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Description = "alpha - preferred",
                    Amount = 200,
                    DueDate = now.AddDays(-(40 - i * 2)),
                    ProductsReceivedAt = now.AddDays(-(39 - i * 2)),
                    IsActive = true
                });
            }

            for (var i = 0; i < 3; i++)
            {
                list.Add(new PayableBill
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Description = "beta - expensive",
                    Amount = 260,
                    DueDate = now.AddDays(-(28 - i * 2)),
                    ProductsReceivedAt = now.AddDays(-(20 - i * 2)),
                    IsActive = true
                });
            }

            return list;
        }

        private static List<PayableBillProduct> BuildSupplierPayableItems(
            IReadOnlyList<PayableBill> bills,
            Product productA,
            Product productB,
            Product productC,
            Product productD)
        {
            var products = new[] { productA, productB, productC, productD };
            var list = new List<PayableBillProduct>();

            foreach (var bill in bills)
            {
                var isAlpha = bill.Description.StartsWith("alpha", StringComparison.OrdinalIgnoreCase);
                var unitValue = isAlpha ? 60 : 75;

                foreach (var product in products)
                {
                    list.Add(new PayableBillProduct
                    {
                        Id = Guid.NewGuid(),
                        PayableBillId = bill.Id,
                        ProductId = product.Id,
                        Quantity = 2,
                        UnitValue = unitValue,
                        LineAmount = unitValue * 2,
                        UnitOfMeasure = "Unit",
                        IsActive = true,
                        CreatedAt = bill.CreatedAt
                    });
                }
            }

            return list;
        }

        private static Order CreateOrder(Guid enterpriseId, Guid customerId, DateTime createdAt, double totalValue, int status)
        {
            return new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                CustomerId = customerId,
                TotalValue = totalValue,
                Status = status,
                CreatedAt = createdAt,
                IsActive = true
            };
        }

        private static OrderedProduct CreateOrderLine(Guid orderId, Guid productId, double quantity, double value)
        {
            return new OrderedProduct
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = productId,
                Quantity = quantity,
                Value = value,
                UnitOfMeasure = "Unit",
                IsActive = true
            };
        }

        private static SutResult BuildSut(
            List<Enterprise>? enterprises = null,
            List<Product>? products = null,
            List<Customer>? customers = null,
            List<Order>? orders = null,
            List<OrderedProduct>? orderedProducts = null,
            List<PayableBill>? payableBills = null,
            List<PayableBillProduct>? payableItems = null,
            List<Opportunity>? opportunities = null,
            List<OpportunitySignal>? signals = null,
            List<OpportunityRecommendation>? recommendations = null,
            List<OpportunityFeedback>? feedbacks = null,
            List<OpportunityRunLog>? runLogs = null)
        {
            var enterpriseStore = enterprises ?? [];
            var productStore = products ?? [];
            var customerStore = customers ?? [];
            var orderStore = orders ?? [];
            var orderLineStore = orderedProducts ?? [];
            var payableBillStore = payableBills ?? [];
            var payableItemStore = payableItems ?? [];
            var opportunityStore = opportunities ?? [];
            var signalStore = signals ?? [];
            var recommendationStore = recommendations ?? [];
            var feedbackStore = feedbacks ?? [];
            var runLogStore = runLogs ?? [];

            var service = new OpportunityRadarService(
                CreateRepositoryMock(opportunityStore).Object,
                CreateRepositoryMock(signalStore).Object,
                CreateRepositoryMock(recommendationStore).Object,
                CreateRepositoryMock(feedbackStore).Object,
                CreateRepositoryMock(runLogStore).Object,
                CreateRepositoryMock(orderStore).Object,
                CreateRepositoryMock(orderLineStore).Object,
                CreateRepositoryMock(productStore).Object,
                CreateRepositoryMock(customerStore).Object,
                CreateRepositoryMock(payableBillStore).Object,
                CreateRepositoryMock(payableItemStore).Object,
                Options.Create(new OpportunityRadarSettings
                {
                    Enabled = true,
                    DefaultHistoryWindowDays = 180,
                    MinimumSampleOrders = 3
                }));

            return new SutResult(service, opportunityStore, feedbackStore);
        }

        private static Mock<IRepository<TEntity>> CreateRepositoryMock<TEntity>(List<TEntity> records)
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

                    return query.ToList();
                });

            repo.Setup(x => x.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<TEntity, bool>>?>(),
                    It.IsAny<Expression<Func<TEntity, object>>?>()))
                .ReturnsAsync((bool descending,
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
                        var compiledOrderBy = orderBy.Compile();
                        query = descending
                            ? query.OrderByDescending(compiledOrderBy)
                            : query.OrderBy(compiledOrderBy);
                    }

                    if (pageNumber.HasValue && pageSize.HasValue && pageNumber.Value > 0 && pageSize.Value > 0)
                        query = query.Skip((pageNumber.Value - 1) * pageSize.Value).Take(pageSize.Value);

                    return query.ToList();
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

        private sealed record SutResult(
            OpportunityRadarService Service,
            List<Opportunity> OpportunityStore,
            List<OpportunityFeedback> FeedbackStore);
    }
}

