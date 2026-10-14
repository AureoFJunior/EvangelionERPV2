using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.DTOs;

namespace EvangelionERPV2.StorefrontModule.Test
{
    // SPEC-limen-design-alive §5.3/§4.3: listing facets, the free-shipping filter, and variant swatches.
    public class StorefrontFacetTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();

        public void Dispose() => _database.Dispose();

        private static StorefrontProductQuery Query(
            string? q = null, string? shop = null, decimal? max = null, bool onSale = false, bool freeShipping = false,
            bool facets = false, string? category = null) =>
            new(q, shop, null, max, false, StorefrontProductSort.Relevance, 1, 50, category, onSale, freeShipping, facets);

        /// <summary>Aurora charges shipping and sells mugs (a two-variant family) and a bowl; Traço ships free and sells a pen and a mug.</summary>
        private async Task<(Enterprise Aurora, Enterprise Traco, StorefrontCategory Mugs, StorefrontCategory Stationery)> SeedAsync()
        {
            var aurora = Build.ListedShop("aurora", "Cerâmica Aurora");
            var traco = Build.ListedShop("traco", "Papelaria Traço");
            traco.StorefrontShippingFee = 0m;
            var house = Build.Category("casa");
            var mugs = Build.Category("canecas", house);
            var stationery = Build.Category("papelaria");
            var family = Guid.NewGuid();
            var mugBlue = Build.Item(aurora, "Caneca", 79m, group: family, label: "Azul", category: mugs.Id, ageDays: 2);
            var mugWhite = Build.Item(aurora, "Caneca", 89m, group: family, label: "Branca", category: mugs.Id, ageDays: 1);
            mugWhite.PictureAdress = "products/branca.png";
            var bowl = Build.Item(aurora, "Tigela", 40m, category: mugs.Id);
            bowl.CompareAtPrice = 60m;
            bowl.PromotionEndsAt = DateTime.UtcNow.AddDays(7);
            var pen = Build.Item(traco, "Lapiseira", 58m, category: stationery.Id);
            var tracoMug = Build.Item(traco, "Caneca de viagem", 120m, category: mugs.Id);
            await _database.SeedAsync(aurora, traco, Build.Fiscal(aurora), Build.Fiscal(traco), house, mugs, stationery,
                mugBlue, mugWhite, bowl, pen, tracoMug);
            return (aurora, traco, mugs, stationery);
        }

        [Fact]
        public async Task Facets_AreLeftOutUnlessAskedFor()
        {
            await SeedAsync();

            Assert.Null((await _database.CreateService().GetProductsAsync(Query())).Facets);
        }

        [Fact]
        public async Task Facets_CountCardsPerShopCategoryFreeShippingAndSale_AFamilyOnce()
        {
            await SeedAsync();

            var page = await _database.CreateService().GetProductsAsync(Query(facets: true));

            var facets = page.Facets!;
            Assert.Equal([("aurora", "Cerâmica Aurora", 2), ("traco", "Papelaria Traço", 2)],
                facets.Stores.Select(s => (s.Slug, s.Name, s.Count)).ToArray());
            Assert.Equal([("canecas", 3), ("papelaria", 1)], facets.Categories.Select(c => (c.Slug, c.Count)).ToArray());
            Assert.Equal((2, 1), (facets.FreeShippingCount, facets.OnSaleCount));
            Assert.Equal(4, page.Total);
        }

        [Fact]
        public async Task Facets_IgnoreTheShopPriceSaleAndShippingFiltersSoEachShowsWhatChoosingItGives()
        {
            await SeedAsync();

            var page = await _database.CreateService().GetProductsAsync(Query(shop: "aurora", max: 50m, onSale: true, freeShipping: false, facets: true));

            Assert.Equal(["aurora", "traco"], page.Facets!.Stores.Select(s => s.Slug).ToArray());
            Assert.Equal(2, page.Facets.FreeShippingCount);
            Assert.Equal("Tigela", Assert.Single(page.Items).Name);
        }

        [Fact]
        public async Task Facets_FollowTheSearchAndTheCategory()
        {
            await SeedAsync();

            var searched = (await _database.CreateService().GetProductsAsync(Query(q: "caneca", facets: true))).Facets!;
            var inStationery = (await _database.CreateService().GetProductsAsync(Query(category: "papelaria", facets: true))).Facets!;

            Assert.Equal([("aurora", 1), ("traco", 1)], searched.Stores.Select(s => (s.Slug, s.Count)).ToArray());
            Assert.Equal(("traco", 1), (Assert.Single(inStationery.Stores).Slug, inStationery.Stores[0].Count));
        }

        [Fact]
        public async Task FreeShipping_KeepsOnlyShopsThatChargeNothing()
        {
            await SeedAsync();

            var page = await _database.CreateService().GetProductsAsync(Query(freeShipping: true));

            Assert.All(page.Items, card => Assert.Equal("traco", card.EnterpriseSlug));
            Assert.Equal(2, page.Total);
        }

        [Fact]
        public async Task Swatches_ListTheFamilysPublishedVariantsInTheirOrder_AndNothingForASingleProduct()
        {
            await SeedAsync();

            var cards = (await _database.CreateService().GetProductsAsync(Query())).Items;

            var mug = cards.Single(c => c.Name == "Caneca");
            Assert.Equal([("Azul", false), ("Branca", true)], mug.Swatches!.Select(s => (s.Label, s.HasPicture)).ToArray());
            Assert.Null(cards.Single(c => c.Name == "Tigela").Swatches);
        }

        [Fact]
        public async Task Swatches_StopAtFive()
        {
            var shop = Build.ListedShop("aurora");
            var family = Guid.NewGuid();
            var variants = Enumerable.Range(1, 7).Select(i => Build.Item(shop, "Vaso", 30m + i, group: family, label: $"V{i}", ageDays: 10 - i)).ToArray();
            await _database.SeedAsync([shop, Build.Fiscal(shop), .. variants]);

            var card = (await _database.CreateService().GetProductsAsync(Query())).Items.Single();

            Assert.Equal(["V1", "V2", "V3", "V4", "V5"], card.Swatches!.Select(s => s.Label).ToArray());
            Assert.Equal(7, card.VariantCount);
        }
    }
}
