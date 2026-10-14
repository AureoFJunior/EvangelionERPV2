using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.StorefrontModule.Test
{
    public class StorefrontCategoryTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();

        public void Dispose() => _database.Dispose();

        private static SaveStorefrontCategoryRequestDTO Save(string slug, Guid? parent = null) => new()
        {
            ParentId = parent,
            Name = "Nova " + slug,
            Slug = slug,
            Icon = "Tag"
        };

        [Fact]
        public async Task Assign_RefusesADepartment_AndMovesAWholeFamily()
        {
            var shop = Build.ListedShop("loja");
            var department = Build.Category("casa");
            var leaf = Build.Category("canecas", department);
            var group = Guid.NewGuid();
            var small = Build.Item(shop, "Caneca", 30m, group: group, label: "P");
            var large = Build.Item(shop, "Caneca", 40m, group: group, label: "G");
            var other = Build.Item(Build.ListedShop("outra"), "Caneca", 40m, group: group, label: "M");
            await _database.SeedAsync(shop, department, leaf, small, large, other);

            var service = _database.CreateCategoryService();
            await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(shop.Id, [small.Id], department.Id));

            Assert.Equal(2, await service.AssignAsync(shop.Id, [small.Id], leaf.Id));

            using var context = _database.CreateContext();
            var rows = await context.Product.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.StorefrontCategoryId);
            Assert.Equal(leaf.Id, rows[large.Id]);
            Assert.Null(rows[other.Id]);
        }

        [Fact]
        public async Task Create_RefusesAChildUnderANodeHoldingProducts_AndADuplicateSlug()
        {
            var shop = Build.ListedShop("loja");
            var leaf = Build.Category("canecas");
            await _database.SeedAsync(shop, leaf, Build.Item(shop, "Caneca", 30m, category: leaf.Id));

            var service = _database.CreateCategoryService();
            var held = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Save("xicaras", leaf.Id)));
            Assert.Contains("1 produtos", held.Message);
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Save("canecas")));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Save("Com Espaço")));
        }

        [Fact]
        public async Task Tree_CountsTheCallersProducts_ButEveryStoreForTheLimenTeam()
        {
            var mine = Build.ListedShop("minha");
            var theirs = Build.ListedShop("deles");
            var department = Build.Category("casa");
            var leaf = Build.Category("canecas", department);
            await _database.SeedAsync(mine, theirs, department, leaf,
                Build.Item(mine, "Caneca", 30m, category: leaf.Id),
                Build.Item(theirs, "Caneca", 30m, category: leaf.Id),
                Build.Item(theirs, "Xícara", 30m, published: false, category: leaf.Id));

            var own = Assert.Single(await _database.CreateCategoryService().GetTreeAsync(mine.Id, includeInactive: false));
            Assert.Equal(1, own.ProductCount);

            var all = Assert.Single(await _database.CreateCategoryService().GetTreeAsync(mine.Id, includeInactive: true));
            Assert.Equal(3, all.ProductCount);
            Assert.Equal(2, all.StoreCount);
        }

        [Fact]
        public async Task Move_RefusesPuttingANodeUnderItsOwnDescendant()
        {
            var department = Build.Category("casa");
            var leaf = Build.Category("canecas", department);
            await _database.SeedAsync(department, leaf);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _database.CreateCategoryService().MoveAsync(department.Id, new MoveStorefrontCategoryRequestDTO { ParentId = leaf.Id }));
        }

        [Fact]
        public async Task Rename_KeepsTheOldSlugAsARedirect()
        {
            var shop = Build.ListedShop("loja");
            await _database.SeedAsync(shop, Build.Fiscal(shop));
            var leaf = Build.Category("canecas");
            await _database.SeedAsync(leaf, Build.Item(shop, "Caneca", 30m, category: leaf.Id));

            await _database.CreateCategoryService().UpdateAsync(leaf.Id, Save("canecas-e-xicaras"));

            var page = await _database.CreateCategoryService().GetPublicPageAsync("canecas");
            Assert.NotNull(page);
            Assert.Equal("canecas-e-xicaras", page.RedirectTo);
            Assert.Equal("canecas-e-xicaras", page.Category.Slug);
            Assert.Null(await _database.CreateCategoryService().GetPublicPageAsync("nada"));
        }

        [Fact]
        public async Task PublicTree_CountsPublishedCardsOfListedShops_DescendantsIncluded()
        {
            var listed = Build.ListedShop("loja");
            var unlisted = Build.ListedShop("fechada", listed: false);
            var department = Build.Category("casa");
            var leaf = Build.Category("canecas", department);
            var hidden = Build.Category("velha", department, active: false);
            var group = Guid.NewGuid();
            await _database.SeedAsync(listed, unlisted, department, leaf, hidden,
                Build.Item(listed, "Caneca", 30m, group: group, label: "P", category: leaf.Id),
                Build.Item(listed, "Caneca", 40m, group: group, label: "G", category: leaf.Id),
                Build.Item(listed, "Prato", 40m, published: false, category: leaf.Id),
                Build.Item(unlisted, "Tigela", 40m, category: leaf.Id),
                Build.Item(listed, "Vaso", 40m, category: hidden.Id));

            var tree = await _database.CreateCategoryService().GetPublicTreeAsync();

            var casa = Assert.Single(tree);
            Assert.Equal(1, casa.ProductCount);
            Assert.Equal("canecas", Assert.Single(casa.Children).Slug);
        }

        [Fact]
        public async Task Catalog_FiltersByDepartmentIncludingChildren_AndShowsTheBreadcrumb()
        {
            var shop = Build.ListedShop("loja");
            var department = Build.Category("casa");
            var leaf = Build.Category("canecas", department);
            var other = Build.Category("livros");
            var mug = Build.Item(shop, "Caneca", 30m, category: leaf.Id);
            await _database.SeedAsync(shop, Build.Fiscal(shop), department, leaf, other, mug, Build.Item(shop, "Livro", 50m, category: other.Id));

            var service = _database.CreateService();
            var page = await service.GetProductsAsync(new StorefrontProductQuery(null, null, null, null, false, StorefrontProductSort.Relevance, 1, 20, "casa"));
            Assert.Equal(mug.Id, Assert.Single(page.Items).Id);

            var none = await service.GetProductsAsync(new StorefrontProductQuery(null, null, null, null, false, StorefrontProductSort.Relevance, 1, 20, "nada"));
            Assert.Empty(none.Items);

            var detail = await service.GetProductAsync(mug.Id);
            Assert.Equal(["casa", "canecas"], detail!.CategoryPath!.Select(p => p.Slug));
        }

        [Fact]
        public async Task Publish_RefusesProductsWithoutACategory_ButWithdrawingNeedsNone()
        {
            var shop = Build.ListedShop("loja");
            var loose = Build.Item(shop, "Caneca solta", 30m, published: false);
            var listed = Build.Item(shop, "Caneca antiga", 30m);
            await _database.SeedAsync(shop, loose, listed);

            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                _database.CreateService().SetPublishedAsync(shop.Id, [loose.Id], published: true));
            Assert.Contains("Caneca solta", error.Message);

            Assert.Equal(1, await _database.CreateService().SetPublishedAsync(shop.Id, [listed.Id], published: false));
        }
    }
}
