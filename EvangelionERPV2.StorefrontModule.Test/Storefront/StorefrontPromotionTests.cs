using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.StorefrontModule.Test
{
    /// <summary>SPEC-limen-pricing-rules §2.1, rule by rule.</summary>
    public class StorefrontPromotionTests : IDisposable
    {
        private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(12);
        private readonly StorefrontTestContext _database = new();

        public void Dispose() => _database.Dispose();

        private sealed class FixedClock(DateTime now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private StorefrontPromotionService Service(DateTime? now = null) => new(_database.CreateContext(), new FixedClock(now ?? Now));

        private static ProductPriceHistory Stretch(Product product, decimal price, int fromDaysAgo, int? toDaysAgo) => new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            EnterpriseId = product.EnterpriseId!.Value,
            Price = price,
            From = Now.AddDays(-fromDaysAgo),
            To = toDaysAgo is { } to ? Now.AddDays(-to) : null,
            IsActive = true
        };

        private static SetStorefrontPromotionRequestDTO Request(Product product, decimal compareAt, int days = 30, bool attested = true) => new()
        {
            ProductId = product.Id,
            CompareAtPrice = compareAt,
            EndsAt = Now.AddDays(days),
            Attested = attested
        };

        /// <summary>Sold at 99,90 from 20 to 5 days ago, then at 79,90.</summary>
        private async Task<(Enterprise Shop, Product Product)> SeedAsync()
        {
            var shop = Build.ListedShop("loja");
            var product = Build.Item(shop, "Caneca", 79.90m);
            await _database.SeedAsync(shop, product, Stretch(product, 99.90m, 20, 5), Stretch(product, 79.90m, 5, null));
            return (shop, product);
        }

        [Fact]
        public async Task A_price_really_practised_for_seven_days_becomes_the_de_price_with_its_attestation()
        {
            var (shop, product) = await SeedAsync();
            var user = Guid.NewGuid();

            var promotion = await Service().SetAsync(shop.Id, user, Request(product, 99.90m));

            Assert.NotNull(promotion);
            Assert.True(promotion.Active);
            Assert.Equal(20, promotion.DiscountPercent);
            Assert.Equal([99.90m], promotion.EligibleCompareAtPrices);
            using var context = _database.CreateContext();
            var saved = await context.Product.SingleAsync(p => p.Id == product.Id);
            Assert.Equal(user, saved.PromotionAttestedByUserId);
            Assert.Equal(Now, saved.PromotionStartedAt);
        }

        [Theory]
        [InlineData(79.90, "maior que o preço atual")]          // P1
        [InlineData(82.00, "pelo menos 5%")]                     // P3
        [InlineData(129.90, "não foi praticado por 7 dias")]     // P2
        public async Task An_invented_or_too_small_de_price_is_refused_naming_the_rule(double compareAt, string reason)
        {
            var (shop, product) = await SeedAsync();

            var error = await Assert.ThrowsAsync<ArgumentException>(() => Service().SetAsync(shop.Id, Guid.NewGuid(), Request(product, (decimal)compareAt)));
            Assert.Contains(reason, error.Message);
        }

        [Fact]
        public async Task A_price_held_only_six_days_or_outside_the_last_thirty_is_not_eligible()
        {
            var shop = Build.ListedShop("loja");
            var product = Build.Item(shop, "Caneca", 79.90m);
            await _database.SeedAsync(shop, product,
                Stretch(product, 119m, 60, 40),
                Stretch(product, 99.90m, 12, 6),
                Stretch(product, 79.90m, 6, null));

            var promotion = await Service().GetAsync(shop.Id, product.Id);
            Assert.Empty(promotion!.EligibleCompareAtPrices);
        }

        [Fact]
        public async Task A_promotion_longer_than_sixty_days_or_without_the_declaration_is_refused()
        {
            var (shop, product) = await SeedAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => Service().SetAsync(shop.Id, Guid.NewGuid(), Request(product, 99.90m, days: 61)));
            await Assert.ThrowsAsync<ArgumentException>(() => Service().SetAsync(shop.Id, Guid.NewGuid(), Request(product, 99.90m, attested: false)));
            Assert.Null(await Service().SetAsync(Guid.NewGuid(), Guid.NewGuid(), Request(product, 99.90m)));
        }

        [Fact]
        public async Task The_catalog_shows_de_por_while_valid_filters_offers_and_hides_an_expired_one()
        {
            var (shop, product) = await SeedAsync();
            await _database.SeedAsync(Build.Fiscal(shop), Build.Item(shop, "Prato", 50m));
            await Service().SetAsync(shop.Id, Guid.NewGuid(), Request(product, 99.90m, days: 10));

            var catalog = _database.CreateService();
            var offers = await catalog.GetProductsAsync(new StorefrontProductQuery(null, null, null, null, false, StorefrontProductSort.Relevance, 1, 20, OnSale: true));
            var card = Assert.Single(offers.Items);
            Assert.Equal((99.90m, 20, 20), (card.CompareAtPrice!.Value, card.DiscountPercent!.Value, card.MaxDiscountPercent!.Value));

            var detail = await catalog.GetProductAsync(product.Id);
            Assert.Equal(99.90m, detail!.CompareAtPrice);
            Assert.Equal([1, 2, 3], detail.Installments!.Select(p => p.Count));
            Assert.Equal(3, card.MaxInstallment!.Count);

            using (var context = _database.CreateContext())
            {
                var row = await context.Product.SingleAsync(p => p.Id == product.Id);
                row.PromotionEndsAt = DateTime.UtcNow.AddMinutes(-1);
                await context.SaveChangesAsync();
            }

            Assert.Null((await _database.CreateService().GetProductAsync(product.Id))!.CompareAtPrice);
            Assert.Empty((await _database.CreateService().GetProductsAsync(new StorefrontProductQuery(null, null, null, null, false, StorefrontProductSort.Relevance, 1, 20, OnSale: true))).Items);
        }

        [Fact]
        public async Task Ending_a_promotion_clears_it()
        {
            var (shop, product) = await SeedAsync();
            await Service().SetAsync(shop.Id, Guid.NewGuid(), Request(product, 99.90m));

            var ended = await Service().EndAsync(shop.Id, product.Id);

            Assert.False(ended!.Active);
            Assert.Null(ended.CompareAtPrice);
        }
    }
}
