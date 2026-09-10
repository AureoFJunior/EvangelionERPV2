using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.ProductModule.Test
{
    /// <summary>
    /// Covers the invariant the ledger exists for: StorageQuantity never moves without a
    /// StockMovement explaining it, and it never goes below zero no matter who races for it.
    /// </summary>
    public class StockLedgerTests : IDisposable
    {
        private readonly StockLedgerTestContext _database = new();

        public void Dispose()
        {
            _database.Dispose();
        }

        [Fact]
        public async Task UpdateForOrder_WritesOneSaleMovementPerLine()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var pens = await SeedProductAsync(context, enterpriseId, "Pens", 10m);
            var paper = await SeedProductAsync(context, enterpriseId, "Paper", 20m);

            var order = BuildOrder(enterpriseId, (pens, 3m), (paper, 4m));

            await service.UpdateForOrder(order);

            Assert.Equal(7m, await ReadStockAsync(context, pens));
            Assert.Equal(16m, await ReadStockAsync(context, paper));

            var movements = await ReadMovementsAsync(context);
            Assert.Equal(2, movements.Count);
            Assert.All(movements, movement =>
            {
                Assert.Equal((int)EnumStockMovementType.Sale, movement.Type);
                Assert.Equal(order.Id, movement.ReferenceId);
                Assert.Equal(enterpriseId, movement.EnterpriseId);
            });

            var pensMovement = movements.Single(x => x.ProductId == pens);
            Assert.Equal(-3m, pensMovement.Quantity);
            Assert.Equal(7m, pensMovement.BalanceAfter);

            var paperMovement = movements.Single(x => x.ProductId == paper);
            Assert.Equal(-4m, paperMovement.Quantity);
            Assert.Equal(16m, paperMovement.BalanceAfter);
        }

        [Fact]
        public async Task UpdateForOrder_WhenQuantityExceedsStock_ThrowsAndWritesNoMovement()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var productId = await SeedProductAsync(context, enterpriseId, "Scarce", 2m);
            var order = BuildOrder(enterpriseId, (productId, 5m));

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.UpdateForOrder(order));

            Assert.Equal(2m, await ReadStockAsync(context, productId));
            Assert.Empty(await ReadMovementsAsync(context));
        }

        [Fact]
        public async Task ApplyStockMovementAsync_WhenTwoCallersRaceForTheLastUnit_OnlyOneWins()
        {
            var enterpriseId = Guid.NewGuid();
            var seedContext = _database.CreateContext();
            var productId = await SeedProductAsync(seedContext, enterpriseId, "Last One", 1m);

            // Two contexts, the way two requests would have two. Both were built while stock read
            // as 1, which is exactly the state a read-modify-write decrement would act on — and
            // exactly the state in which it would sell the same unit twice.
            var first = CreateService(_database.CreateContext());
            var second = CreateService(_database.CreateContext());

            await first.ApplyStockMovementAsync(productId, enterpriseId, -1m, EnumStockMovementType.Sale, Guid.NewGuid());

            await Assert.ThrowsAsync<InsertDatabaseException>(() =>
                second.ApplyStockMovementAsync(productId, enterpriseId, -1m, EnumStockMovementType.Sale, Guid.NewGuid()));

            Assert.Equal(0m, await ReadStockAsync(seedContext, productId));
            Assert.Single(await ReadMovementsAsync(seedContext));
        }

        [Fact]
        public async Task ApplyStockMovementAsync_UnderParallelDecrements_NeverOversells()
        {
            const int stock = 3;
            const int callers = 6;

            var enterpriseId = Guid.NewGuid();
            var seedContext = _database.CreateContext();
            var productId = await SeedProductAsync(seedContext, enterpriseId, "Contended", stock);

            var services = Enumerable.Range(0, callers)
                .Select(_ => CreateService(_database.CreateContext()))
                .ToList();

            var attempts = services.Select(async service =>
            {
                try
                {
                    await service.ApplyStockMovementAsync(productId, enterpriseId, -1m, EnumStockMovementType.Sale, Guid.NewGuid());
                    return true;
                }
                catch (Exception)
                {
                    // Rejected for want of stock, or lost a write lock to a sibling. Either way
                    // the assertions below only care that nothing was sold that did not exist.
                    return false;
                }
            });

            var winners = (await Task.WhenAll(attempts)).Count(succeeded => succeeded);
            var remaining = await ReadStockAsync(seedContext, productId);
            var movements = await ReadMovementsAsync(seedContext);

            Assert.True(winners <= stock, $"{winners} callers took stock, but only {stock} units existed.");
            Assert.Equal(stock - winners, remaining);
            Assert.Equal(winners, movements.Count);
            Assert.Equal(-winners, movements.Sum(movement => movement.Quantity));
        }

        [Fact]
        public async Task ApplyStockMovementAsync_ForARefund_PutsStockBackAndRecordsWhy()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            var productId = await SeedProductAsync(context, enterpriseId, "Returned", 5m);

            await service.ApplyStockMovementAsync(productId, enterpriseId, -2m, EnumStockMovementType.Sale, orderId);
            await service.ApplyStockMovementAsync(productId, enterpriseId, 2m, EnumStockMovementType.Refund, orderId);

            Assert.Equal(5m, await ReadStockAsync(context, productId));

            var movements = (await ReadMovementsAsync(context)).OrderBy(x => x.Type).ToList();
            Assert.Equal(2, movements.Count);
            Assert.Equal(3m, movements[0].BalanceAfter);
            Assert.Equal((int)EnumStockMovementType.Refund, movements[1].Type);
            Assert.Equal(2m, movements[1].Quantity);
            Assert.Equal(5m, movements[1].BalanceAfter);
            Assert.All(movements, movement => Assert.Equal(orderId, movement.ReferenceId));
        }

        [Fact]
        public async Task ApplyStockMovementAsync_ForAnotherEnterprise_ChangesNothing()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var productId = await SeedProductAsync(context, enterpriseId, "Tenanted", 4m);

            await Assert.ThrowsAsync<InsertDatabaseException>(() =>
                service.ApplyStockMovementAsync(productId, Guid.NewGuid(), -1m, EnumStockMovementType.Sale, null));

            Assert.Equal(4m, await ReadStockAsync(context, productId));
            Assert.Empty(await ReadMovementsAsync(context));
        }

        private static ProductService CreateService(AppDbContext context)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret"
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}"
                });

            // A real repository, not a mock: the ledger write and the quantity change have to land
            // in one transaction, and a mocked ExecuteInTransactionAsync would never open one.
            return new ProductService(
                new Repository<Product>(context),
                configuration,
                new AWSKMSKeyProvider(secretsManager.Object, configuration),
                new Mock<IProductReportGeneratorService>().Object,
                context);
        }

        /// <summary>
        /// allowNegative is the single door to a negative balance, and only the PDV offline sync
        /// holds the key: that sale already happened and the customer already left with the goods.
        /// </summary>
        [Fact]
        public async Task ApplyStockMovementAsync_WithAllowNegative_IsTheOnlyWayBelowZero()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var productId = await SeedProductAsync(context, enterpriseId, "Sold While Offline", 2m);

            var movement = await service.ApplyStockMovementAsync(
                productId, enterpriseId, -5m, EnumStockMovementType.Sale, Guid.NewGuid(), allowNegative: true);

            // -3 is the honest record: three units left the shelf that the books never knew about.
            Assert.Equal(-3m, await ReadStockAsync(context, productId));
            Assert.Equal(-3m, movement.BalanceAfter);
            Assert.Equal(-5m, movement.Quantity);
        }

        /// <summary>
        /// The same oversell without the flag — every path but the till — is refused outright.
        /// </summary>
        [Fact]
        public async Task ApplyStockMovementAsync_WithoutAllowNegative_RefusesToGoBelowZero()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var productId = await SeedProductAsync(context, enterpriseId, "Scarce", 2m);

            await Assert.ThrowsAsync<InsertDatabaseException>(() =>
                service.ApplyStockMovementAsync(
                    productId, enterpriseId, -5m, EnumStockMovementType.Adjustment, null));

            Assert.Equal(2m, await ReadStockAsync(context, productId));
            Assert.Empty(await ReadMovementsAsync(context));
        }

        /// <summary>
        /// The other half of the offline sale: a human counts the shelf and corrects the negative
        /// the till left behind. The correction is a movement like any other, not an assignment.
        /// </summary>
        [Fact]
        public async Task ApplyStockMovementAsync_ForAnAdjustment_BringsANegativeCountBack()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var productId = await SeedProductAsync(context, enterpriseId, "Recounted", 0m);
            await service.ApplyStockMovementAsync(
                productId, enterpriseId, -4m, EnumStockMovementType.Sale, Guid.NewGuid(), allowNegative: true);

            // The shelf actually holds 6, so the correction is +10 against the -4 on the books.
            var correction = await service.ApplyStockMovementAsync(
                productId, enterpriseId, 10m, EnumStockMovementType.Adjustment, null);

            Assert.Equal(6m, await ReadStockAsync(context, productId));
            Assert.Equal(6m, correction.BalanceAfter);

            var movements = await ReadMovementsAsync(context);
            Assert.Equal(2, movements.Count);
            Assert.Single(movements, x => x.Type == (int)EnumStockMovementType.Adjustment);
        }

        [Fact]
        public async Task ApplyStockMovementAsync_WhenThePeriodIsClosed_WritesNothing()
        {
            // The ledger is always stamped now, so the only period it can violate is today's.
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var pens = await SeedProductAsync(context, enterpriseId, "Pens", 10m);
            await SeedEnterpriseAsync(context, enterpriseId, DateTime.UtcNow.Date);

            await Assert.ThrowsAsync<PeriodClosedException>(
                () => service.ApplyStockMovementAsync(pens, enterpriseId, -1m, EnumStockMovementType.Sale, null));

            Assert.Equal(10m, await ReadStockAsync(context, pens));
            Assert.Empty(await ReadMovementsAsync(context));
        }

        [Fact]
        public async Task ApplyStockMovementAsync_WhenTheLockIsInThePast_StillMoves()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);
            var enterpriseId = Guid.NewGuid();

            var pens = await SeedProductAsync(context, enterpriseId, "Pens", 10m);
            await SeedEnterpriseAsync(context, enterpriseId, DateTime.UtcNow.Date.AddDays(-1));

            await service.ApplyStockMovementAsync(pens, enterpriseId, -1m, EnumStockMovementType.Sale, null);

            Assert.Equal(9m, await ReadStockAsync(context, pens));
        }

        /// <summary>
        /// Raw INSERT rather than the DbSet: Enterprise carries store-generated defaults
        /// (Currency, CurrentBalance) that the fixture's minimal DDL does not declare, so EF's
        /// RETURNING clause reads NULL back and the save blows up.
        /// </summary>
        private static async Task SeedEnterpriseAsync(AppDbContext context, Guid enterpriseId, DateTime? closedThroughUtc)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO Enterprise (Id, Name, PhoneNumber, Email, Adress, Currency, CurrentBalance, ShouldSendMonthlyBilling, ShouldSendDunning, ClosedThroughUtc, CreatedAt, IsActive)
                   VALUES ({enterpriseId}, 'Nerv', '', '', '', 'BRL', 0, 0, 0, {closedThroughUtc}, {DateTime.UtcNow}, 1)");

            context.ChangeTracker.Clear();
        }

        private static async Task<Guid> SeedProductAsync(AppDbContext context, Guid enterpriseId, string name, decimal storageQuantity)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = name,
                Description = name,
                UnitOfMeasure = nameof(EnumUnitOfMeasure.Unit),
                DefaultValue = 10m,
                StorageQuantity = storageQuantity,
                IsActive = true
            };

            context.Product.Add(product);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            return product.Id;
        }

        private static Order BuildOrder(Guid enterpriseId, params (Guid ProductId, decimal Quantity)[] lines)
        {
            return new Order(
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(2),
                100,
                enterpriseId,
                Guid.NewGuid(),
                lines.Select(line => new OrderedProduct
                {
                    Id = Guid.NewGuid(),
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    Value = 10,
                    IsActive = true
                }).ToList(),
                Guid.NewGuid())
            {
                Id = Guid.NewGuid()
            };
        }

        private static Task<decimal> ReadStockAsync(AppDbContext context, Guid productId)
        {
            return context.Product.AsNoTracking()
                .Where(product => product.Id == productId)
                .Select(product => product.StorageQuantity)
                .FirstAsync();
        }

        private static async Task<List<StockMovement>> ReadMovementsAsync(AppDbContext context)
        {
            return await context.StockMovement.AsNoTracking().ToListAsync();
        }
    }
}
