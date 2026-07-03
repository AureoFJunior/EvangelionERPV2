using EvangelionERPV2.OpportunityRadarModule.Application.Configs;
using EvangelionERPV2.OpportunityRadarModule.Application.Services;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.OpportunityRadarModule.Test
{
    public class OpportunityRadarIntegrationTests
    {
        [Fact]
        public async Task RecomputeAsync_ShouldPersistOpportunityGraph_AndRunLog()
        {
            using var sut = BuildSut();
            var context = sut.Context;
            var service = sut.Service;
            var enterpriseId = sut.EnterpriseId;

            var run = await service.RecomputeAsync(
                enterpriseId,
                null,
                new OpportunityRecomputeRequestDTO { HistoryWindowDays = 90 },
                "manual");

            Assert.Equal("completed", run.Status);

            var opportunities = await context.Opportunity.AsNoTracking().Where(x => x.EnterpriseId == enterpriseId).ToListAsync();
            Assert.NotEmpty(opportunities);

            var opportunityIds = opportunities.Select(x => x.Id).ToHashSet();
            var signals = await context.OpportunitySignal.AsNoTracking().Where(x => opportunityIds.Contains(x.OpportunityId)).ToListAsync();
            var recommendations = await context.OpportunityRecommendation.AsNoTracking().Where(x => opportunityIds.Contains(x.OpportunityId)).ToListAsync();
            var runLog = await context.OpportunityRunLog.AsNoTracking().FirstOrDefaultAsync(x => x.RunId == run.RunId);

            Assert.NotEmpty(signals);
            Assert.NotEmpty(recommendations);
            Assert.NotNull(runLog);
            Assert.Equal("completed", runLog!.Status);
            Assert.True(runLog.TotalGenerated > 0 || runLog.TotalUpdated > 0);
        }

        [Fact]
        public async Task RecomputeAsync_WhenExecutedTwice_ShouldBeIdempotentByFingerprint()
        {
            using var sut = BuildSut();
            var context = sut.Context;
            var service = sut.Service;
            var enterpriseId = sut.EnterpriseId;

            var first = await service.RecomputeAsync(
                enterpriseId,
                null,
                new OpportunityRecomputeRequestDTO { HistoryWindowDays = 90 },
                "manual");

            var countAfterFirst = await context.Opportunity.AsNoTracking().CountAsync(x => x.EnterpriseId == enterpriseId && x.IsActive == true);
            Assert.True(countAfterFirst > 0);

            var second = await service.RecomputeAsync(
                enterpriseId,
                null,
                new OpportunityRecomputeRequestDTO { HistoryWindowDays = 90 },
                "manual");

            var countAfterSecond = await context.Opportunity.AsNoTracking().CountAsync(x => x.EnterpriseId == enterpriseId && x.IsActive == true);
            var runLogsCount = await context.OpportunityRunLog.AsNoTracking().CountAsync(x => x.EnterpriseId == enterpriseId);

            Assert.Equal(countAfterFirst, countAfterSecond);
            Assert.True(first.TotalGenerated > 0 || first.TotalUpdated > 0);
            Assert.Equal(0, second.TotalGenerated);
            Assert.True(second.TotalUpdated > 0);
            Assert.Equal(2, runLogsCount);
        }

        [Fact]
        public async Task AddFeedbackAsync_ShouldEnforcePermissionAndPersistFeedback()
        {
            using var sut = BuildSut();
            var context = sut.Context;
            var service = sut.Service;
            var enterpriseId = sut.EnterpriseId;

            var opportunity = new Opportunity
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Type = "cross_sell",
                Status = "new",
                Title = "Opportunity",
                Description = "Opportunity",
                SourceRule = "rule",
                SourceModel = "rules-v1",
                Fingerprint = "it-feedback-fingerprint",
                RunId = Guid.NewGuid(),
                IsActive = true
            };

            context.Opportunity.Add(opportunity);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.AddFeedbackAsync(
                enterpriseId,
                opportunity.Id,
                null,
                new OpportunityFeedbackRequestDTO { Status = "accepted" },
                canApproveExecution: false));

            var feedback = await service.AddFeedbackAsync(
                enterpriseId,
                opportunity.Id,
                null,
                new OpportunityFeedbackRequestDTO
                {
                    Status = "rejected",
                    Comment = "Not aligned with current strategy."
                },
                canApproveExecution: false);

            Assert.Equal("rejected", feedback.Status);

            var persistedFeedbacks = await context.OpportunityFeedback.AsNoTracking()
                .Where(x => x.OpportunityId == opportunity.Id && x.IsActive == true)
                .ToListAsync();
            var refreshedOpportunity = await context.Opportunity.AsNoTracking().FirstAsync(x => x.Id == opportunity.Id);

            Assert.Single(persistedFeedbacks);
            Assert.Equal("archived", refreshedOpportunity.Status);
        }

        private static IntegrationSut BuildSut()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"opportunity-radar-tests-{Guid.NewGuid():N}")
                .Options;

            var context = new AppDbContext(options, httpContextAccessor: null, auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
            context.Database.EnsureCreated();

            var enterpriseId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var productAId = Guid.NewGuid();
            var productBId = Guid.NewGuid();

            var enterprise = new Enterprise
            {
                Id = enterpriseId,
                Name = "EVA",
                Currency = "BRL",
                IsActive = true
            };

            var customer = new Customer
            {
                Id = customerId,
                EnterpriseId = enterpriseId,
                Name = "Customer",
                Document = "12345678000199",
                Adress = "Sao Paulo, SP",
                IsActive = true
            };

            var productA = new Product
            {
                Id = productAId,
                EnterpriseId = enterpriseId,
                Name = "Product A",
                DefaultValue = 120,
                StorageQuantity = 45,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var productB = new Product
            {
                Id = productBId,
                EnterpriseId = enterpriseId,
                Name = "Product B",
                DefaultValue = 90,
                StorageQuantity = 52,
                UnitOfMeasure = "Unit",
                IsActive = true
            };

            var now = DateTime.UtcNow;
            var orders = new[]
            {
                new Order { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CustomerId = customerId, CreatedAt = now.AddDays(-26), TotalValue = 210, Status = 2, IsActive = true },
                new Order { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CustomerId = customerId, CreatedAt = now.AddDays(-16), TotalValue = 210, Status = 3, IsActive = true },
                new Order { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CustomerId = customerId, CreatedAt = now.AddDays(-6), TotalValue = 210, Status = 4, IsActive = true }
            };

            var orderLines = orders.SelectMany(order => new[]
            {
                new OrderedProduct
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = productAId,
                    Quantity = 1,
                    Value = 120,
                    UnitOfMeasure = "Unit",
                    IsActive = true
                },
                new OrderedProduct
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = productBId,
                    Quantity = 1,
                    Value = 90,
                    UnitOfMeasure = "Unit",
                    IsActive = true
                }
            }).ToList();

            var payableBill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Description = "alpha - supplier",
                Amount = 220,
                DueDate = now.AddDays(-20),
                ProductsReceivedAt = now.AddDays(-19),
                IsActive = true
            };

            var payableItems = new[]
            {
                new PayableBillProduct
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = payableBill.Id,
                    ProductId = productAId,
                    Quantity = 4,
                    UnitValue = 40,
                    LineAmount = 160,
                    UnitOfMeasure = "Unit",
                    IsActive = true
                },
                new PayableBillProduct
                {
                    Id = Guid.NewGuid(),
                    PayableBillId = payableBill.Id,
                    ProductId = productBId,
                    Quantity = 1,
                    UnitValue = 60,
                    LineAmount = 60,
                    UnitOfMeasure = "Unit",
                    IsActive = true
                }
            };

            context.Enterprise.Add(enterprise);
            context.Customer.Add(customer);
            context.Product.AddRange(productA, productB);
            context.Order.AddRange(orders);
            context.OrderedProduct.AddRange(orderLines);
            context.PayableBill.Add(payableBill);
            context.PayableBillProduct.AddRange(payableItems);
            context.SaveChanges();

            var service = new OpportunityRadarService(
                new Repository<Opportunity>(context),
                new Repository<OpportunitySignal>(context),
                new Repository<OpportunityRecommendation>(context),
                new Repository<OpportunityFeedback>(context),
                new Repository<OpportunityRunLog>(context),
                new Repository<Order>(context),
                new Repository<OrderedProduct>(context),
                new Repository<Product>(context),
                new Repository<Customer>(context),
                new Repository<PayableBill>(context),
                new Repository<PayableBillProduct>(context),
                Options.Create(new OpportunityRadarSettings
                {
                    Enabled = true,
                    DefaultHistoryWindowDays = 180,
                    MinimumSampleOrders = 3
                }));

            return new IntegrationSut(context, service, enterpriseId);
        }

        private sealed class NoopAuditTrailEntryFactory : IAuditTrailEntryFactory
        {
            public IReadOnlyCollection<AuditTrail> Create(
                IEnumerable<EntityEntry<BaseEntity>> entries,
                Guid? userId,
                Guid? enterpriseId,
                DateTime changedAt)
            {
                return Array.Empty<AuditTrail>();
            }
        }

        private sealed class IntegrationSut : IDisposable
        {
            public IntegrationSut(AppDbContext context, OpportunityRadarService service, Guid enterpriseId)
            {
                Context = context;
                Service = service;
                EnterpriseId = enterpriseId;
            }

            public AppDbContext Context { get; }
            public OpportunityRadarService Service { get; }
            public Guid EnterpriseId { get; }

            public void Dispose()
            {
                Context.Dispose();
            }
        }
    }
}
