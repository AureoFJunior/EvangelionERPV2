using EvangelionERPV2.StorefrontModule.Application.DTOs;

namespace EvangelionERPV2.StorefrontModule.Test
{
    public class StorefrontListingTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();

        public void Dispose() => _database.Dispose();

        private static SetStorefrontListingRequestDTO Request(bool listed = true, string? slug = "aurora") => new()
        {
            IsListed = listed,
            Slug = slug,
            Description = "Louça feita à mão.",
            ShippingFee = 32.5m,
            HandlingDays = 2,
            DeliveryDaysMin = 3,
            DeliveryDaysMax = 7
        };

        [Fact]
        public async Task Listing_IsRefusedUntilStripeAndFiscalIdentityExist_ButTheProfileIsKept()
        {
            var shop = Build.ListedShop("x", listed: false);
            shop.StorefrontSlug = null;
            shop.StripeConnectedAccountId = null;
            await _database.SeedAsync(shop);

            var service = _database.CreateService();

            var error = await Assert.ThrowsAsync<ArgumentException>(() => service.SetListingAsync(shop.Id, Request()));
            Assert.Contains("Stripe", error.Message);
            Assert.Contains("CNPJ", error.Message);

            var draft = await service.SetListingAsync(shop.Id, Request(listed: false));
            Assert.False(draft.IsListed);
            Assert.Equal("aurora", draft.Slug);
            Assert.Equal(2, draft.MissingRequirements.Count);
        }

        [Fact]
        public async Task Listing_SucceedsWhenEverythingIsInPlace_AndKeepsTheFirstListingDate()
        {
            var shop = Build.ListedShop("x", listed: false);
            shop.StorefrontListedAt = null;
            await _database.SeedAsync(shop, Build.Fiscal(shop));

            var service = _database.CreateService();

            var listed = await service.SetListingAsync(shop.Id, Request());
            Assert.True(listed.IsListed);
            Assert.Empty(listed.MissingRequirements);
            var since = listed.ListedAt;
            Assert.NotNull(since);

            await service.SetListingAsync(shop.Id, Request(listed: false));
            var relisted = await _database.CreateService().SetListingAsync(shop.Id, Request());
            Assert.Equal(since, relisted.ListedAt);

            Assert.NotNull(await _database.CreateService().GetEnterpriseAsync("aurora"));
        }

        [Fact]
        public async Task Listing_RefusesTheSlugOfAnotherShop_AndMalformedSlugs()
        {
            var taken = Build.ListedShop("aurora");
            var shop = Build.ListedShop("x", listed: false);
            await _database.SeedAsync(taken, shop, Build.Fiscal(shop));

            var service = _database.CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.SetListingAsync(shop.Id, Request(slug: "aurora")));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetListingAsync(shop.Id, Request(slug: "Com Espaço")));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetListingAsync(shop.Id, Request(slug: "-x-")));
        }

        [Fact]
        public async Task Listing_RefusesThePlatformEnterprise()
        {
            var platform = Build.ListedShop("limen", listed: false);
            platform.Id = StorefrontTestContext.PlatformEnterpriseId;
            await _database.SeedAsync(platform, Build.Fiscal(platform));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _database.CreateService().SetListingAsync(platform.Id, Request(slug: "limen")));
        }

        [Fact]
        public async Task Listing_RefusesATransitRangeThatRunsBackwards()
        {
            var shop = Build.ListedShop("x");
            await _database.SeedAsync(shop, Build.Fiscal(shop));

            var request = Request();
            request.DeliveryDaysMin = 8;

            await Assert.ThrowsAsync<ArgumentException>(() => _database.CreateService().SetListingAsync(shop.Id, request));
        }

        [Fact]
        public async Task SetPublished_TouchesOnlyTheCallersOwnProducts()
        {
            var mine = Build.ListedShop("minha");
            var theirs = Build.ListedShop("deles");
            var leaf = Build.Category("canecas");
            var own = Build.Item(mine, "Caneca", 50m, published: false, category: leaf.Id);
            var foreign = Build.Item(theirs, "Prato", 60m, published: false, category: leaf.Id);
            await _database.SeedAsync(leaf, mine, theirs, own, foreign);

            var changed = await _database.CreateService().SetPublishedAsync(mine.Id, [own.Id, foreign.Id], published: true);

            Assert.Equal(1, changed);
            var service = _database.CreateService();
            Assert.NotNull(await service.GetProductAsync(own.Id));
            Assert.Null(await service.GetProductAsync(foreign.Id));
        }
    }
}
