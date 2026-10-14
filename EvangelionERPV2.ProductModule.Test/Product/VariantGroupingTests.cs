using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EvangelionERPV2.ProductModule.Test
{
    public class VariantGroupingTests : IDisposable
    {
        private readonly StockLedgerTestContext _database = new();

        public void Dispose() => _database.Dispose();

        [Fact]
        public async Task UpdateAsync_WhenGroupAlreadyBelongsToAnotherEnterprise_ThrowsArgumentException()
        {
            var group = Guid.NewGuid();
            var otherEnterprise = Guid.NewGuid();
            var ownEnterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            seedContext.Product.Add(NewProduct("Camiseta Gola V", otherEnterprise, group, "Preto / M"));
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var mine = NewProduct("Camiseta Gola V", ownEnterprise, group, "Preto / G");
            context.Product.Add(mine);
            await context.SaveChangesAsync();

            var service = CreateService(context);

            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(mine));
        }

        [Fact]
        public async Task UpdateAsync_WhenGroupStaysInsideOneEnterprise_Succeeds()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            seedContext.Product.Add(NewProduct("Camiseta Gola V", enterprise, group, "Preto / M"));
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var sibling = NewProduct("Camiseta Gola V", enterprise, group, "Preto / G");
            context.Product.Add(sibling);
            await context.SaveChangesAsync();

            var updated = await CreateService(context).UpdateAsync(sibling);

            Assert.Equal(group, updated.VariantGroupId);
            Assert.Equal("Preto / G", updated.VariantLabel);
        }

        [Fact]
        public async Task UpdateAsync_WhenAnotherActiveSiblingUsesTheSameLabel_ThrowsArgumentException()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            seedContext.Product.Add(NewProduct("Camiseta Gola V", enterprise, group, "Preto / M"));
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var duplicate = NewProduct("Camiseta Gola V", enterprise, group, "preto / m");
            context.Product.Add(duplicate);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => CreateService(context).UpdateAsync(duplicate));
        }

        [Fact]
        public async Task UpdateAsync_WhenTheClashingSiblingIsInactive_Succeeds()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            var retired = NewProduct("Camiseta Gola V", enterprise, group, "Preto / M");
            retired.IsActive = false;
            seedContext.Product.Add(retired);
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var replacement = NewProduct("Camiseta Gola V", enterprise, group, "Preto / M");
            context.Product.Add(replacement);
            await context.SaveChangesAsync();

            var updated = await CreateService(context).UpdateAsync(replacement);

            Assert.Equal("Preto / M", updated.VariantLabel);
        }

        [Fact]
        public async Task UpdateAsync_WhenProductIsStandalone_SkipsTheGroupCheck()
        {
            var context = _database.CreateContext();
            var standalone = NewProduct("Caneca", Guid.NewGuid(), null, null);
            context.Product.Add(standalone);
            await context.SaveChangesAsync();

            var updated = await CreateService(context).UpdateAsync(standalone);

            Assert.Null(updated.VariantGroupId);
            Assert.Null(updated.VariantLabel);
        }

        /// <summary>The name is the family's: a new variant takes it, whatever the form sent.</summary>
        [Fact]
        public async Task CreateAsync_JoiningAnExistingGroup_TakesTheFamilyName()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            seedContext.Product.Add(NewProduct("Camiseta Gola V", enterprise, group, "Preto / M"));
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var created = await CreateService(context).CreateAsync(new ProductPicture
            {
                Product = NewProduct("camiseta gola v preta G", enterprise, group, "Preto / G")
            });

            Assert.Equal("Camiseta Gola V", created.Name);
        }

        [Fact]
        public async Task CreateAsync_StartingANewGroup_KeepsTheTypedName()
        {
            var context = _database.CreateContext();
            var created = await CreateService(context).CreateAsync(new ProductPicture
            {
                Product = NewProduct("Moletom Canguru", Guid.NewGuid(), Guid.NewGuid(), "Cinza / P")
            });

            Assert.Equal("Moletom Canguru", created.Name);
        }

        /// <summary>Renaming any variant renames its family, so the family never shows two names.</summary>
        [Fact]
        public async Task UpdateAsync_RenamingAVariant_RenamesEveryVariantOfItsFamilyOnly()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            var sibling = NewProduct("Camiseta Gola V", enterprise, group, "Preto / M");
            var retired = NewProduct("Camiseta Gola V", enterprise, group, "Preto / GG");
            retired.IsActive = false;
            var otherFamily = NewProduct("Camiseta Gola V", enterprise, Guid.NewGuid(), "Azul / M");
            seedContext.Product.AddRange(sibling, retired, otherFamily);
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var renamed = NewProduct("Camiseta Gola V", enterprise, group, "Preto / G");
            context.Product.Add(renamed);
            await context.SaveChangesAsync();

            renamed.Name = "Camiseta Gola V Algodão";
            await CreateService(context).UpdateAsync(renamed);

            using var fresh = _database.CreateContext();
            Assert.Equal("Camiseta Gola V Algodão", fresh.Product.Single(p => p.Id == sibling.Id).Name);
            Assert.Equal("Camiseta Gola V Algodão", fresh.Product.Single(p => p.Id == retired.Id).Name);
            Assert.Equal("Camiseta Gola V Algodão", fresh.Product.Single(p => p.Id == renamed.Id).Name);
            Assert.Equal("Camiseta Gola V", fresh.Product.Single(p => p.Id == otherFamily.Id).Name);
        }

        /// <summary>Moving a product into a family is joining it: the product takes the name, the family keeps it.</summary>
        [Fact]
        public async Task UpdateAsync_MovingAProductIntoAFamily_TakesTheFamilyNameAndRenamesNobody()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();

            using var seedContext = _database.CreateContext();
            var member = NewProduct("Camiseta Gola V", enterprise, group, "Preto / M");
            seedContext.Product.Add(member);
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var loose = NewProduct("Camiseta preta G avulsa", enterprise, null, null);
            context.Product.Add(loose);
            await context.SaveChangesAsync();

            loose.VariantGroupId = group;
            loose.VariantLabel = "Preto / G";
            var moved = await CreateService(context).UpdateAsync(loose);

            using var fresh = _database.CreateContext();
            Assert.Equal("Camiseta Gola V", moved.Name);
            Assert.Equal("Camiseta Gola V", fresh.Product.Single(p => p.Id == loose.Id).Name);
            Assert.Equal("Camiseta Gola V", fresh.Product.Single(p => p.Id == member.Id).Name);
        }

        /// <summary>SPEC-limen-categories C4: a family files under one category; a joining product takes it.</summary>
        [Fact]
        public async Task UpdateAsync_ChangingTheCategory_MovesTheFamily_AndAJoiningProductTakesIt()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();
            var department = new StorefrontCategory { Id = Guid.NewGuid(), Name = "Casa", Slug = "casa", IsActive = true };
            var mugs = new StorefrontCategory { Id = Guid.NewGuid(), ParentId = department.Id, Name = "Canecas", Slug = "canecas", IsActive = true };
            var plates = new StorefrontCategory { Id = Guid.NewGuid(), ParentId = department.Id, Name = "Pratos", Slug = "pratos", IsActive = true };

            using var seedContext = _database.CreateContext();
            var sibling = NewProduct("Caneca", enterprise, group, "P");
            sibling.StorefrontCategoryId = mugs.Id;
            seedContext.AddRange(department, mugs, plates, sibling);
            await seedContext.SaveChangesAsync();

            var context = _database.CreateContext();
            var edited = NewProduct("Caneca", enterprise, group, "G");
            edited.StorefrontCategoryId = mugs.Id;
            context.Product.Add(edited);
            await context.SaveChangesAsync();

            edited.StorefrontCategoryId = department.Id;
            await Assert.ThrowsAsync<ArgumentException>(() => CreateService(context).UpdateAsync(edited));

            edited.StorefrontCategoryId = plates.Id;
            await CreateService(context).UpdateAsync(edited);

            var joining = await CreateService(_database.CreateContext()).CreateAsync(new ProductPicture
            {
                Product = NewProduct("caneca m", enterprise, group, "M")
            });

            using var fresh = _database.CreateContext();
            Assert.Equal(plates.Id, fresh.Product.Single(p => p.Id == sibling.Id).StorefrontCategoryId);
            Assert.Equal(plates.Id, joining.StorefrontCategoryId);
        }

        [Fact]
        public async Task CreateAsync_FilingAVariantOfAnUncategorizedFamily_FilesTheWholeFamily()
        {
            var group = Guid.NewGuid();
            var enterprise = Guid.NewGuid();
            var leaf = new StorefrontCategory { Id = Guid.NewGuid(), Name = "Canecas", Slug = "canecas", IsActive = true };

            using var seedContext = _database.CreateContext();
            var sibling = NewProduct("Caneca", enterprise, group, "P");
            seedContext.AddRange(leaf, sibling);
            await seedContext.SaveChangesAsync();

            var created = NewProduct("Caneca", enterprise, group, "G");
            created.StorefrontCategoryId = leaf.Id;
            await CreateService(_database.CreateContext()).CreateAsync(new ProductPicture { Product = created });

            using var fresh = _database.CreateContext();
            Assert.Equal(leaf.Id, fresh.Product.Single(p => p.Id == sibling.Id).StorefrontCategoryId);
        }

        /// <summary>SPEC-limen-pricing-rules §2.2 and P5: every price change is recorded; reaching the "de" ends the promotion.</summary>
        [Fact]
        public async Task UpdateAsync_ChangingThePrice_RecordsItsHistory_AndReachingTheDePriceEndsThePromotion()
        {
            var enterprise = Guid.NewGuid();
            var created = await CreateService(_database.CreateContext()).CreateAsync(new ProductPicture
            {
                Product = NewProduct("Caneca", enterprise, null, null)
            });

            var context = _database.CreateContext();
            var product = context.Product.Single(p => p.Id == created.Id);
            product.CompareAtPrice = 69.9m;
            product.PromotionEndsAt = DateTime.UtcNow.AddDays(10);
            product.DefaultValue = 69.9m;
            await CreateService(context).UpdateAsync(product);

            using var fresh = _database.CreateContext();
            var history = fresh.ProductPriceHistory.Where(h => h.ProductId == created.Id).OrderBy(h => h.From).ToList();
            Assert.Equal([59.9m, 69.9m], history.Select(h => h.Price));
            Assert.NotNull(history[0].To);
            Assert.Null(history[1].To);
            Assert.Null(fresh.Product.Single(p => p.Id == created.Id).CompareAtPrice);
        }

        private static Product NewProduct(string name, Guid enterpriseId, Guid? group, string? label) =>
            new(name, "Descricao", 59.9m, 10, false, false, null, enterpriseId)
            {
                Id = Guid.NewGuid(),
                IsActive = true,
                VariantGroupId = group,
                VariantLabel = label
            };

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

            return new ProductService(
                new Repository<Product>(context),
                configuration,
                new AWSKMSKeyProvider(secretsManager.Object, configuration),
                new Mock<IProductReportGeneratorService>().Object,
                context);
        }
    }
}
