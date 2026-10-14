using EvangelionERPV2.ProductModule.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.StorefrontModule.Test
{
    // Promotions, categories and family sync write product columns without the product repository,
    // so a product edit must read the row fresh, never from the 60-minute cache it then writes back.
    public class ProductCacheTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();

        public void Dispose() => _database.Dispose();

        [Fact]
        public async Task GetFreshByIdAsync_SeesAChangeMadeAroundTheCache_WhileGetByIdAsyncStillServesTheCachedCopy()
        {
            var shop = Build.ListedShop("aurora");
            var mug = Build.Item(shop, "Caneca", 79m);
            await _database.SeedAsync(shop, mug);
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            await using var context = _database.CreateContext();
            var repository = new ProductRepository(context, cache);
            await repository.GetByIdAsync(mug.Id);

            await using (var other = _database.CreateContext())
            {
                await other.Product.Where(p => p.Id == mug.Id).ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.CompareAtPrice, 99m)
                    .SetProperty(p => p.PromotionEndsAt, DateTime.UtcNow.AddDays(7)));
            }

            Assert.Null((await repository.GetByIdAsync(mug.Id)).CompareAtPrice);
            var fresh = await repository.GetFreshByIdAsync(mug.Id);
            Assert.Equal(99m, fresh.CompareAtPrice);
            Assert.NotNull(fresh.PromotionEndsAt);
        }
    }
}
