using System.Linq.Expressions;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.OrderModule.Test
{
    /// <summary>
    /// Orçamento: a promise that costs nothing until it becomes an order.
    /// </summary>
    public class QuoteServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();

        [Fact]
        public async Task Create_PricesTheLinesAndTotalsThem()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var (service, quotes, items, _) = BuildSut(products: [product]);

            var quote = await service.CreateAsync(EnterpriseId, new UpsertQuoteRequestDTO
            {
                Items = [new QuoteItemRequestDTO { ProductId = product.Id, Quantity = 3, UnitValue = 90m }]
            });

            Assert.Single(quotes);
            Assert.Equal(270m, quote.TotalValue);
            Assert.Equal((int)EnumQuoteStatus.Draft, quote.Status);

            var line = Assert.Single(items);
            // The quoted price stands: quoting a discount is most of the point.
            Assert.Equal(90m, line.UnitValue);
            Assert.Equal(270m, line.LineAmount);
            // The unit comes from the catalogue, never from the request.
            Assert.Equal("Unit", line.UnitOfMeasure);
        }

        [Fact]
        public async Task Create_FallsBackToTheCatalaguePrice_WhenNoneIsQuoted()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var (service, _, items, _) = BuildSut(products: [product]);

            await service.CreateAsync(EnterpriseId, new UpsertQuoteRequestDTO
            {
                Items = [new QuoteItemRequestDTO { ProductId = product.Id, Quantity = 2 }]
            });

            Assert.Equal(100m, Assert.Single(items).UnitValue);
        }

        [Fact]
        public async Task Create_RefusesAProductFromAnotherTenant()
        {
            var foreign = BuildProduct("Plugsuit", defaultValue: 100m);
            foreign.EnterpriseId = Guid.NewGuid();

            var (service, _, _, _) = BuildSut(products: [foreign]);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.CreateAsync(EnterpriseId, new UpsertQuoteRequestDTO
            {
                Items = [new QuoteItemRequestDTO { ProductId = foreign.Id, Quantity = 1 }]
            }));
        }

        [Fact]
        public async Task Create_TakesItsNumberFromTheTenantsCounter()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 10m);
            var (service, _, _, _) = BuildSut(products: [product], nextNumber: 41);

            var quote = await service.CreateAsync(EnterpriseId, NewRequest(product));

            Assert.Equal(41, quote.Number);
        }

        [Fact]
        public async Task Update_OfASentQuote_IsRefused()
        {
            // The customer is holding those lines; editing them under their feet starts a dispute.
            var product = BuildProduct("Plugsuit", defaultValue: 10m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Sent);
            var (service, _, _, _) = BuildSut(products: [product], stored: quote);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.UpdateAsync(quote.Id, EnterpriseId, NewRequest(product)));
        }

        [Fact]
        public async Task Convert_CreatesAnOrderCarryingTheQuotedPrices_AndLinksIt()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Accepted, product, quantity: 2, unitValue: 80m);
            var (service, _, _, orderService) = BuildSut(products: [product], stored: quote);

            Order? created = null;
            orderService
                .Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>()))
                .ReturnsAsync((Order order, bool _) =>
                {
                    order.Id = Guid.NewGuid();
                    created = order;
                    return order;
                });

            var converted = await service.ConvertAsync(quote.Id, EnterpriseId, new ConvertQuoteRequestDTO());

            Assert.NotNull(created);
            var line = Assert.Single(created!.OrderedProduct!);
            Assert.Equal(80m, line.Value);
            Assert.Equal(2m, line.Quantity);

            Assert.Equal(created.Id, converted.ConvertedOrderId);
            Assert.Equal((int)EnumQuoteStatus.Converted, converted.Status);
        }

        [Fact]
        public async Task Convert_Twice_IsRefused()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Accepted, product);
            quote.ConvertedOrderId = Guid.NewGuid();

            var (service, _, _, _) = BuildSut(products: [product], stored: quote);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.ConvertAsync(quote.Id, EnterpriseId, new ConvertQuoteRequestDTO()));
        }

        [Fact]
        public async Task Convert_OfAnExpiredQuote_IsRefused()
        {
            // Expired is read off the clock, not off the column — nothing sweeps quotes.
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Sent, product);
            quote.ValidUntil = DateTime.UtcNow.Date.AddDays(-1);

            var (service, _, _, orderService) = BuildSut(products: [product], stored: quote);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.ConvertAsync(quote.Id, EnterpriseId, new ConvertQuoteRequestDTO()));

            orderService.Verify(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task Convert_LetsTheOrderGatesRefuse_WithoutTouchingTheQuote()
        {
            // A closed period or a credit limit refuses the order; the quote must stay convertible
            // rather than end up marked Converted with no order behind it.
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Accepted, product);
            var (service, _, _, orderService) = BuildSut(products: [product], stored: quote);

            orderService
                .Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>()))
                .ThrowsAsync(new CreditLimitExceededException("over the limit"));

            await Assert.ThrowsAsync<CreditLimitExceededException>(
                () => service.ConvertAsync(quote.Id, EnterpriseId, new ConvertQuoteRequestDTO()));

            Assert.Null(quote.ConvertedOrderId);
            Assert.Equal((int)EnumQuoteStatus.Accepted, quote.Status);
        }

        [Fact]
        public async Task Convert_PassesTheCreditOverrideThrough()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Accepted, product);
            var (service, _, _, orderService) = BuildSut(products: [product], stored: quote);

            bool? seen = null;
            orderService
                .Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>()))
                .ReturnsAsync((Order order, bool allowOverride) =>
                {
                    order.Id = Guid.NewGuid();
                    seen = allowOverride;
                    return order;
                });

            await service.ConvertAsync(quote.Id, EnterpriseId, new ConvertQuoteRequestDTO(), allowCreditOverride: true);

            Assert.True(seen);
        }

        [Fact]
        public async Task Delete_OfAConvertedQuote_IsRefused()
        {
            var product = BuildProduct("Plugsuit", defaultValue: 100m);
            var quote = BuildStoredQuote(EnumQuoteStatus.Converted, product);
            quote.ConvertedOrderId = Guid.NewGuid();

            var (service, _, _, _) = BuildSut(products: [product], stored: quote);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.DeleteAsync(quote.Id, EnterpriseId));
        }

        [Fact]
        public void StatusAsOf_ReadsExpiryOffTheClock()
        {
            var quote = BuildStoredQuote(EnumQuoteStatus.Sent);
            quote.ValidUntil = new DateTime(2026, 3, 31);

            Assert.Equal(EnumQuoteStatus.Sent, quote.StatusAsOf(new DateTime(2026, 3, 31)));
            Assert.Equal(EnumQuoteStatus.Expired, quote.StatusAsOf(new DateTime(2026, 4, 1)));

            // A closed quote stays closed: expiry never overwrites a decision somebody made.
            quote.Status = (int)EnumQuoteStatus.Rejected;
            Assert.Equal(EnumQuoteStatus.Rejected, quote.StatusAsOf(new DateTime(2026, 4, 1)));
        }

        private static UpsertQuoteRequestDTO NewRequest(Product product) => new()
        {
            Items = [new QuoteItemRequestDTO { ProductId = product.Id, Quantity = 1, UnitValue = 10m }]
        };

        private static Product BuildProduct(string name, decimal defaultValue) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            Name = name,
            UnitOfMeasure = nameof(EnumUnitOfMeasure.Unit),
            DefaultValue = defaultValue,
            IsActive = true
        };

        private static Quote BuildStoredQuote(
            EnumQuoteStatus status,
            Product? product = null,
            decimal quantity = 1m,
            decimal unitValue = 100m)
        {
            var quote = new Quote
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                Number = 7,
                Status = (int)status,
                IssuedAt = DateTime.UtcNow.Date,
                ValidUntil = DateTime.UtcNow.Date.AddDays(10),
                TotalValue = quantity * unitValue,
                IsActive = true
            };

            if (product != null)
            {
                quote.Items =
                [
                    new QuoteItem
                    {
                        Id = Guid.NewGuid(),
                        QuoteId = quote.Id,
                        ProductId = product.Id,
                        Quantity = quantity,
                        UnitValue = unitValue,
                        LineAmount = quantity * unitValue,
                        UnitOfMeasure = product.UnitOfMeasure,
                        IsActive = true
                    }
                ];
            }

            return quote;
        }

        private static (QuoteService Service, List<Quote> Quotes, List<QuoteItem> Items, Mock<IOrderService<Order>> OrderService)
            BuildSut(List<Product>? products = null, Quote? stored = null, long nextNumber = 1)
        {
            var quoteStore = stored == null ? [] : new List<Quote> { stored };
            var itemStore = stored?.Items?.ToList() ?? [];
            var productStore = products ?? [];

            var quoteRepository = new Mock<IQuoteRepository>();
            quoteRepository.Setup(x => x.AllocateNextNumberAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(nextNumber);
            quoteRepository.Setup(x => x.GetWithItemsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid enterpriseId, CancellationToken _) =>
                    quoteStore.FirstOrDefault(x => x.Id == id && x.EnterpriseId == enterpriseId));
            quoteRepository.Setup(x => x.CreateAsync(It.IsAny<Quote>()))
                .ReturnsAsync((Quote quote) => { quoteStore.Add(quote); return quote; });
            quoteRepository.Setup(x => x.Update(It.IsAny<Quote>())).Returns((Quote quote) => quote);
            quoteRepository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var itemRepository = new Mock<IRepository<QuoteItem>>();
            itemRepository.Setup(x => x.CreateRangeAsync(It.IsAny<IEnumerable<QuoteItem>>()))
                .ReturnsAsync((IEnumerable<QuoteItem> lines) =>
                {
                    var materialised = lines.ToList();
                    itemStore.AddRange(materialised);
                    return materialised;
                });
            itemRepository.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<QuoteItem, bool>>?>()))
                .ReturnsAsync((Expression<Func<QuoteItem, bool>>? predicate) =>
                    predicate == null ? itemStore : itemStore.Where(predicate.Compile()).ToList());
            itemRepository.Setup(x => x.DeleteRange(It.IsAny<IEnumerable<QuoteItem>>()))
                .Returns((IEnumerable<QuoteItem> lines) =>
                {
                    var materialised = lines.ToList();
                    foreach (var line in materialised)
                        itemStore.Remove(line);

                    return materialised;
                });

            var productRepository = new Mock<IRepository<Product>>();
            productRepository.Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Product, bool>>?>(),
                    It.IsAny<Func<IQueryable<Product>, IOrderedQueryable<Product>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<Product, bool>>? predicate,
                    Func<IQueryable<Product>, IOrderedQueryable<Product>>? _,
                    int? __,
                    int? ___) =>
                {
                    var items = predicate == null ? productStore : productStore.Where(predicate.Compile()).ToList();
                    return ((IReadOnlyList<Product>)items, items.Count);
                });

            var orderService = new Mock<IOrderService<Order>>();

            var service = new QuoteService(
                quoteRepository.Object,
                itemRepository.Object,
                productRepository.Object,
                orderService.Object);

            return (service, quoteStore, itemStore, orderService);
        }
    }
}
