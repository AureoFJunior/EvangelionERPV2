using EvangelionERPV2.OrderModule.Domain.Repositories;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace EvangelionERPV2.OrderModule.Test
{
    /// <summary>
    /// The cached order must not outlive the row it copies.
    /// </summary>
    /// <remarks>
    /// GetByIdAsync serves from Redis for an hour and nothing used to clear it, so a soft-deleted
    /// order kept answering 200 as if it were live — the API returned isActive:true while the
    /// database held IsActive = 0. Cypress found it; no unit test could, because every one of them
    /// mocked the repository away.
    /// </remarks>
    public class OrderRepositoryCacheTests
    {
        [Fact]
        public void Update_DropsTheCachedCopy()
        {
            var (repository, cache, order) = BuildRepository();

            repository.Update(order);

            cache.Verify(x => x.Remove($"Order:v2:{order.Id}"), Times.Once);
        }

        [Fact]
        public void Delete_DropsTheCachedCopy()
        {
            var (repository, cache, order) = BuildRepository();

            repository.Delete(order);

            cache.Verify(x => x.Remove($"Order:v2:{order.Id}"), Times.Once);
        }

        [Fact]
        public void UpdateRange_DropsEveryCachedCopy()
        {
            var (repository, cache, first) = BuildRepository();
            var second = new Order { Id = Guid.NewGuid(), EnterpriseId = first.EnterpriseId, IsActive = true };

            repository.UpdateRange([first, second]);

            cache.Verify(x => x.Remove($"Order:v2:{first.Id}"), Times.Once);
            cache.Verify(x => x.Remove($"Order:v2:{second.Id}"), Times.Once);
        }

        [Fact]
        public void Update_SurvivesACacheThatIsDown()
        {
            // A Redis outage must not stop an order being saved; the stale read is the lesser harm.
            var (repository, cache, order) = BuildRepository();
            cache.Setup(x => x.Remove(It.IsAny<string>())).Throws(new InvalidOperationException("redis is gone"));

            repository.Update(order);
        }

        private static (OrderRepository Repository, Mock<IDistributedCache> Cache, Order Order) BuildRepository()
        {
            // A provider that is never connected to: Update and Delete only touch the change
            // tracker, and the point of these tests is the cache key, not the database.
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
                .Options;

            var context = new AppDbContext(options);
            var cache = new Mock<IDistributedCache>();
            var order = new Order { Id = Guid.NewGuid(), EnterpriseId = Guid.NewGuid(), IsActive = true };

            return (new OrderRepository(context, cache.Object), cache, order);
        }
    }
}
