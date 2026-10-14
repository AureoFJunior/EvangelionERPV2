using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using Moq;

namespace EvangelionERPV2.StorefrontModule.Test
{
    public class StorefrontCatalogTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();

        public void Dispose() => _database.Dispose();

        private static StorefrontProductQuery Query(
            string? q = null, string? shop = null, decimal? min = null, decimal? max = null, bool available = false,
            StorefrontProductSort sort = StorefrontProductSort.Relevance, int page = 1, int size = 50) =>
            new(q, shop, min, max, available, sort, page, size);

        [Fact]
        public async Task UnlistedEnterprise_IsInvisibleThroughEveryCatalogRead()
        {
            var hidden = Build.ListedShop("escondida", listed: false);
            var item = Build.Item(hidden, "Caneca", 50m);
            item.PictureAdress = "products/caneca.jpg";
            await _database.SeedAsync(hidden, Build.Fiscal(hidden), item);

            var service = _database.CreateService();

            Assert.Null(await service.GetEnterpriseAsync("escondida"));
            Assert.Empty((await service.GetEnterprisesAsync(1, 50)).Items);
            Assert.Empty((await service.GetProductsAsync(Query())).Items);
            Assert.Empty((await service.GetProductsAsync(Query(shop: "escondida"))).Items);
            Assert.Null(await service.GetProductAsync(item.Id));
            Assert.Null(await service.GetPictureAsync(item.Id));
            _database.ProductService.Verify(p => p.GetPictureBase64Async(It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task InactiveEnterprise_IsInvisibleEvenWhenListed()
        {
            var shop = Build.ListedShop("fechada");
            shop.IsActive = false;
            var item = Build.Item(shop, "Caneca", 50m);
            await _database.SeedAsync(shop, item);

            var service = _database.CreateService();

            Assert.Null(await service.GetEnterpriseAsync("fechada"));
            Assert.Null(await service.GetProductAsync(item.Id));
        }

        [Fact]
        public async Task UnpublishedProduct_IsInvisibleButStaysInItsGroupPickerAsUnavailable()
        {
            var shop = Build.ListedShop("aurora");
            var group = Guid.NewGuid();
            var blue = Build.Item(shop, "Caneca", 79m, group: group, label: "Azul", ageDays: 2);
            var white = Build.Item(shop, "Caneca", 79m, published: false, group: group, label: "Branca", ageDays: 1);
            await _database.SeedAsync(shop, Build.Fiscal(shop), blue, white);

            var service = _database.CreateService();

            Assert.Null(await service.GetProductAsync(white.Id));
            Assert.DoesNotContain((await service.GetProductsAsync(Query())).Items, card => card.Id == white.Id);

            var detail = await service.GetProductAsync(blue.Id);
            Assert.NotNull(detail);
            Assert.Equal(1, detail!.VariantCount);
            Assert.Equal(
                [("Azul", true), ("Branca", false)],
                detail.Variants.Select(v => (v.Label, v.Available)).ToList());
        }

        [Fact]
        public async Task Products_AreOneCardPerGroup_PricedFromTheCheapest_ShownThroughTheCheapestAvailable()
        {
            var shop = Build.ListedShop("traco");
            var group = Guid.NewGuid();
            var cheapButOut = Build.Item(shop, "Caderno", 60m, stock: 0, group: group, label: "Preto");
            var green = Build.Item(shop, "Caderno", 69.9m, group: group, label: "Verde");
            var terracota = Build.Item(shop, "Caderno", 74.9m, group: group, label: "Terracota");
            var standalone = Build.Item(shop, "Lapiseira", 58m);
            await _database.SeedAsync(shop, Build.Fiscal(shop), cheapButOut, green, terracota, standalone);

            var page = await _database.CreateService().GetProductsAsync(Query());

            Assert.Equal(2, page.Total);
            var card = Assert.Single(page.Items, c => c.Name == "Caderno");
            Assert.Equal(green.Id, card.Id);
            Assert.Equal(60m, card.Price);
            Assert.True(card.PriceFrom);
            Assert.Equal(3, card.VariantCount);
            Assert.True(card.Available);

            var single = Assert.Single(page.Items, c => c.Name == "Lapiseira");
            Assert.False(single.PriceFrom);
            Assert.Equal(1, single.VariantCount);
        }

        [Fact]
        public async Task Search_IgnoresAccentsCaseOrderAndPlural_AndRanksNamePrefixFirst()
        {
            var shop = Build.ListedShop("serra");
            var suporte = Build.Item(shop, "Suporte para café coado", 40m, ageDays: 0);
            var cafe = Build.Item(shop, "Café especial Bourbon", 55m, ageDays: 5);
            var caneca = Build.Item(shop, "Caneca esmaltada", 79m);
            await _database.SeedAsync(shop, Build.Fiscal(shop), suporte, cafe, caneca);

            var service = _database.CreateService();

            var byPrefix = await service.GetProductsAsync(Query(q: "CAFE"));
            Assert.Equal([cafe.Id, suporte.Id], byPrefix.Items.Select(c => c.Id).ToList());

            var anyOrder = await service.GetProductsAsync(Query(q: "bourbon cafés"));
            Assert.Equal(cafe.Id, Assert.Single(anyOrder.Items).Id);

            var plural = await service.GetProductsAsync(Query(q: "canecas"));
            Assert.Equal(caneca.Id, Assert.Single(plural.Items).Id);
        }

        [Fact]
        public async Task Search_MatchesTheVariantLabel()
        {
            var shop = Build.ListedShop("aurora");
            var group = Guid.NewGuid();
            await _database.SeedAsync(shop,
                Build.Item(shop, "Caneca", 79m, group: group, label: "Azul"),
                Build.Item(shop, "Caneca", 79m, group: group, label: "Terracota"));

            var page = await _database.CreateService().GetProductsAsync(Query(q: "caneca terracota"));

            Assert.Single(page.Items);
        }

        [Fact]
        public async Task Filters_AndPaging_WorkOnCards()
        {
            var shop = Build.ListedShop("ervilha");
            var other = Build.ListedShop("outra");
            await _database.SeedAsync(shop, other,
                Build.Item(shop, "A", 10m, ageDays: 3),
                Build.Item(shop, "B", 20m, stock: 0, ageDays: 2),
                Build.Item(shop, "C", 30m, ageDays: 1),
                Build.Item(other, "D", 25m));

            var service = _database.CreateService();

            var inShop = await service.GetProductsAsync(Query(shop: "ervilha", sort: StorefrontProductSort.PriceAsc));
            Assert.Equal(["A", "B", "C"], inShop.Items.Select(c => c.Name).ToList());

            var availableInRange = await service.GetProductsAsync(Query(min: 15m, max: 30m, available: true, sort: StorefrontProductSort.PriceDesc));
            Assert.Equal(["C", "D"], availableInRange.Items.Select(c => c.Name).ToList());

            var second = await service.GetProductsAsync(Query(sort: StorefrontProductSort.Newest, page: 2, size: 2));
            Assert.Equal(4, second.Total);
            Assert.Equal(["B", "A"], second.Items.Select(c => c.Name).ToList());
        }

        [Fact]
        public async Task Enterprise_CarriesLegalIdentityDeliveryRangeAndCardCount()
        {
            var shop = Build.ListedShop("aurora", "Cerâmica Aurora");
            var group = Guid.NewGuid();
            await _database.SeedAsync(shop, Build.Fiscal(shop),
                Build.Item(shop, "Caneca", 79m, group: group, label: "Azul"),
                Build.Item(shop, "Caneca", 79m, group: group, label: "Verde"),
                Build.Item(shop, "Prato", 118m),
                Build.Item(shop, "Vaso", 150m, published: false));

            var profile = await _database.CreateService().GetEnterpriseAsync("aurora");

            Assert.NotNull(profile);
            Assert.Equal("Cerâmica Aurora Ltda.", profile!.LegalName);
            Assert.Equal("11222333000181", profile.Cnpj);
            Assert.Equal(("Curitiba", "PR"), (profile.City, profile.Uf));
            Assert.Equal((5, 9), (profile.DeliveryDaysMin, profile.DeliveryDaysMax));
            Assert.Equal(2, profile.ProductCount);
            Assert.Equal(new DateOnly(2026, 9, 1), profile.ListedSince);
        }

        [Fact]
        public async Task Product_CapsMaxPerOrder_AndHasNoneWhenOutOfStock()
        {
            var shop = Build.ListedShop("serra");
            var plenty = Build.Item(shop, "Café", 50m, stock: 400);
            var few = Build.Item(shop, "Kit", 89m, stock: 3.5m);
            var none = Build.Item(shop, "Descafeinado", 58m, stock: 0);
            await _database.SeedAsync(shop, plenty, few, none);

            var service = _database.CreateService();

            Assert.Equal(10, (await service.GetProductAsync(plenty.Id))!.MaxPerOrder);
            Assert.Equal(3, (await service.GetProductAsync(few.Id))!.MaxPerOrder);
            var soldOut = (await service.GetProductAsync(none.Id))!;
            Assert.False(soldOut.Available);
            Assert.Equal(0, soldOut.MaxPerOrder);
        }

        [Fact]
        public async Task Picture_IsReadForAPublishedProduct_WithItsType()
        {
            var shop = Build.ListedShop("serra");
            var item = Build.Item(shop, "Café", 50m);
            item.PictureAdress = "products/cafe.png";
            await _database.SeedAsync(shop, item);

            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };
            _database.ProductService
                .Setup(p => p.GetPictureBase64Async("products/cafe.png"))
                .ReturnsAsync(Convert.ToBase64String(png));

            var picture = await _database.CreateService().GetPictureAsync(item.Id);

            Assert.NotNull(picture);
            Assert.Equal("image/png", picture!.ContentType);
            Assert.Equal(png, picture.Content);
        }

        [Fact]
        public async Task AFamilyCard_ShowsTheFamilyPhoto_ElseAnyVariantPhoto_NeverThePlaceholderWhileOneExists()
        {
            var shop = Build.ListedShop("aurora");
            var withFamilyPhoto = Guid.NewGuid();
            var withVariantPhoto = Guid.NewGuid();
            var cheapNoPhoto = Build.Item(shop, "Caneca", 50m, group: withVariantPhoto, label: "Azul");
            var dearWithPhoto = Build.Item(shop, "Caneca", 90m, group: withVariantPhoto, label: "Branca");
            dearWithPhoto.PictureAdress = "products/branca.png";
            var bowl = Build.Item(shop, "Tigela", 40m, group: withFamilyPhoto, label: "P");
            var bowlLarge = Build.Item(shop, "Tigela", 60m, group: withFamilyPhoto, label: "G");
            await _database.SeedAsync(shop, Build.Fiscal(shop), cheapNoPhoto, dearWithPhoto, bowl, bowlLarge,
                new ProductVariantGroup { Id = withFamilyPhoto, EnterpriseId = shop.Id, PictureAdress = "product-families/tigela.png", IsActive = true });

            var cards = (await _database.CreateService().GetProductsAsync(Query())).Items;

            var mug = cards.Single(c => c.Name == "Caneca");
            Assert.Equal((cheapNoPhoto.Id, true, dearWithPhoto.Id, (Guid?)null), (mug.Id, mug.HasPicture, mug.PictureProductId, mug.FamilyPictureId));
            var tigela = cards.Single(c => c.Name == "Tigela");
            Assert.Equal((true, (Guid?)null, (Guid?)withFamilyPhoto), (tigela.HasPicture, tigela.PictureProductId, tigela.FamilyPictureId));
        }

        [Fact]
        public async Task TheProductPage_SaysWhichVariantsHavePhotos_AndPointsAtTheFamilyPhoto()
        {
            var shop = Build.ListedShop("aurora");
            var group = Guid.NewGuid();
            var blue = Build.Item(shop, "Caneca", 79m, group: group, label: "Azul");
            blue.PictureAdress = "products/azul.png";
            var white = Build.Item(shop, "Caneca", 79m, group: group, label: "Branca");
            await _database.SeedAsync(shop, Build.Fiscal(shop), blue, white,
                new ProductVariantGroup { Id = group, EnterpriseId = shop.Id, PictureAdress = "product-families/caneca.png", IsActive = true });

            var detail = (await _database.CreateService().GetProductAsync(white.Id))!;

            Assert.Equal((false, (Guid?)null, (Guid?)group), (detail.HasPicture, detail.PictureProductId, detail.FamilyPictureId));
            Assert.True(detail.Variants.Single(v => v.Id == blue.Id).HasPicture);
            Assert.False(detail.Variants.Single(v => v.Id == white.Id).HasPicture);
        }

        [Fact]
        public async Task AFamilyPhoto_IsServedOnlyForAPublishedFamily()
        {
            var shop = Build.ListedShop("aurora");
            var published = Guid.NewGuid();
            var hidden = Guid.NewGuid();
            await _database.SeedAsync(shop, Build.Fiscal(shop),
                Build.Item(shop, "Caneca", 79m, group: published, label: "Azul"),
                Build.Item(shop, "Prato", 30m, published: false, group: hidden, label: "P"),
                new ProductVariantGroup { Id = published, EnterpriseId = shop.Id, PictureAdress = "a", IsActive = true },
                new ProductVariantGroup { Id = hidden, EnterpriseId = shop.Id, PictureAdress = "b", IsActive = true });
            _database.ProductService.Setup(p => p.GetPictureBase64Async("a")).ReturnsAsync(Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

            var photo = await _database.CreateService().GetFamilyPictureAsync(published);

            Assert.Equal("image/png", photo!.ContentType);
            Assert.Null(await _database.CreateService().GetFamilyPictureAsync(hidden));
            Assert.Null(await _database.CreateService().GetFamilyPictureAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task TwoShopsUsingTheSameGroupId_GetTwoCards_AndEachOnlyItsOwnFamilyPhoto()
        {
            var aurora = Build.ListedShop("aurora", "Aurora");
            var traco = Build.ListedShop("traco", "Traço");
            var shared = Guid.NewGuid();
            await _database.SeedAsync(aurora, traco, Build.Fiscal(aurora), Build.Fiscal(traco),
                Build.Item(aurora, "Caneca", 50m, group: shared, label: "Azul"),
                Build.Item(traco, "Lapiseira", 30m, group: shared, label: "0,5"),
                new ProductVariantGroup { Id = shared, EnterpriseId = aurora.Id, PictureAdress = "a", IsActive = true });

            var cards = (await _database.CreateService().GetProductsAsync(Query())).Items;

            Assert.Equal(2, cards.Count);
            Assert.Equal(shared, cards.Single(c => c.EnterpriseSlug == "aurora").FamilyPictureId);
            Assert.Null(cards.Single(c => c.EnterpriseSlug == "traco").FamilyPictureId);
        }

        [Fact]
        public async Task AFamilyPhoto_IsNotServedThroughAnotherShopThatReusedItsGroupId()
        {
            var hidden = Build.ListedShop("escondida", listed: false);
            var other = Build.ListedShop("traco");
            var shared = Guid.NewGuid();
            await _database.SeedAsync(hidden, other, Build.Fiscal(other),
                Build.Item(hidden, "Caneca", 50m, group: shared, label: "Azul"),
                Build.Item(other, "Lapiseira", 30m, group: shared, label: "0,5"),
                new ProductVariantGroup { Id = shared, EnterpriseId = hidden.Id, PictureAdress = "secret", IsActive = true });
            _database.ProductService.Setup(p => p.GetPictureBase64Async("secret")).ReturnsAsync(Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

            Assert.Null(await _database.CreateService().GetFamilyPictureAsync(shared));
        }

        private static Order StorefrontSale(Enterprise shop, Product product, decimal units, int daysAgo = 1, EnumOrderStatus status = EnumOrderStatus.Paid)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = shop.Id,
                Channel = (int)EnumOrderChannel.Storefront,
                Status = (int)status,
                CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
                IsActive = true
            };
            order.OrderedProduct = [new OrderedProduct { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = product.Id, Quantity = units, Value = product.DefaultValue, IsActive = true }];
            return order;
        }

        [Fact]
        public async Task Best_selling_counts_a_familys_units_together_and_leaves_out_refunds_old_and_unsold()
        {
            var shop = Build.ListedShop("loja");
            var group = Guid.NewGuid();
            var small = Build.Item(shop, "Caneca", 30m, group: group, label: "P");
            var large = Build.Item(shop, "Caneca", 40m, group: group, label: "G");
            var plate = Build.Item(shop, "Prato", 50m);
            var bowl = Build.Item(shop, "Tigela", 50m);
            var unsold = Build.Item(shop, "Vaso", 50m);
            await _database.SeedAsync(shop, Build.Fiscal(shop), small, large, plate, bowl, unsold,
                StorefrontSale(shop, small, 2), StorefrontSale(shop, large, 2), StorefrontSale(shop, plate, 3),
                StorefrontSale(shop, bowl, 9, status: EnumOrderStatus.Refund), StorefrontSale(shop, bowl, 9, daysAgo: 40));

            var page = await _database.CreateService().GetProductsAsync(
                new StorefrontProductQuery(null, null, null, null, false, StorefrontProductSort.BestSelling, 1, 20));

            Assert.Equal(["Caneca", "Prato"], page.Items.Select(card => card.Name));
        }

        [Fact]
        public async Task Low_stock_is_a_flag_for_three_or_fewer_never_for_a_service()
        {
            var shop = Build.ListedShop("loja");
            var few = Build.Item(shop, "Caneca", 30m, stock: 2);
            var plenty = Build.Item(shop, "Prato", 30m, stock: 9);
            await _database.SeedAsync(shop, Build.Fiscal(shop), few, plenty);

            var cards = (await _database.CreateService().GetProductsAsync(
                new StorefrontProductQuery(null, null, null, null, false, StorefrontProductSort.Relevance, 1, 20))).Items.ToDictionary(c => c.Name);
            Assert.True(cards["Caneca"].LowStock);
            Assert.False(cards["Prato"].LowStock);
            Assert.True((await _database.CreateService().GetProductAsync(few.Id))!.LowStock);
        }
    }
}
