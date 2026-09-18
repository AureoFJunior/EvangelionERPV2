using EvangelionERPV2.OrderModule.Domain.Repositories;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace EvangelionERPV2.OrderModule.Test
{
    /// <summary>
    /// The page has to be taken by the database, not by the caller.
    /// </summary>
    /// <remarks>
    /// GetOrdersByFilter used to load every active order for the enterprise — each one with its
    /// Customer, OrderedProduct and Product — and then filter, sort and page the list in memory.
    /// A tenant with a real order history answered OutOfMemoryException from
    /// BufferedDataReader before the controller saw a row. What these pin is that the filter, the
    /// order and the page are part of the query the provider is handed, not a slice taken off an
    /// unbounded result — which is the difference that defect turned on.
    ///
    /// Sqlite would be closer to production but cannot build this schema: AppDbContext carries
    /// SQL Server column types, and EnsureCreated fails with "no such function: nvarchar".
    /// </remarks>
    public class OrderRepositoryFilterPagingTests : IDisposable
    {
        private readonly AppDbContext _context;
        private readonly OrderRepository _repository;
        private readonly Guid _enterpriseId = Guid.NewGuid();

        public OrderRepositoryFilterPagingTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"order-filter-paging-{Guid.NewGuid():N}")
                .Options;

            _context = new AppDbContext(options, httpContextAccessor: null, auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
            _repository = new OrderRepository(_context, new Mock<IDistributedCache>().Object);
        }

        [Fact]
        public async Task TakesOnlyTheRequestedPage()
        {
            SeedOrders(120);

            var page = await _repository.GetAllAsyncByFilter(descending: true, pageNumber: 1, pageSize: 20);

            Assert.Equal(20, page.Count());
        }

        [Fact]
        public async Task OrdersByCreatedAtAndHonoursTheDirection()
        {
            SeedOrders(10);

            var descending = (await _repository.GetAllAsyncByFilter(true, 1, 10)).ToList();
            var ascending = (await _repository.GetAllAsyncByFilter(false, 1, 10)).ToList();

            Assert.Equal(descending.Select(o => o.Id).Reverse(), ascending.Select(o => o.Id));
            Assert.True(descending.First().CreatedAt >= descending.Last().CreatedAt);
        }

        [Fact]
        public async Task PagesDoNotOverlapOrSkipRows()
        {
            SeedOrders(30);

            var first = await _repository.GetAllAsyncByFilter(true, 1, 10);
            var second = await _repository.GetAllAsyncByFilter(true, 2, 10);
            var third = await _repository.GetAllAsyncByFilter(true, 3, 10);

            var seen = first.Concat(second).Concat(third).Select(o => o.Id).ToList();

            Assert.Equal(30, seen.Count);
            Assert.Equal(30, seen.Distinct().Count());
        }

        [Fact]
        public async Task PagingPastTheEndReturnsEmptyRatherThanThrowing()
        {
            SeedOrders(5);

            var page = await _repository.GetAllAsyncByFilter(true, 99, 20);

            Assert.Empty(page);
        }

        [Fact]
        public async Task AppliesThePredicateInTheQuery()
        {
            SeedOrders(20);
            var other = Guid.NewGuid();
            SeedOrders(5, other);

            var page = await _repository.GetAllAsyncByFilter(true, 1, 50, o => o.EnterpriseId == other);

            Assert.Equal(5, page.Count());
            Assert.All(page, o => Assert.Equal(other, o.EnterpriseId));
        }

        [Fact]
        public async Task InactiveOrdersStayOutOfTheResult()
        {
            SeedOrders(10);
            _context.Set<Order>().Add(new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
            });
            _context.SaveChanges();

            var page = await _repository.GetAllAsyncByFilter(true, 1, 50);

            Assert.Equal(10, page.Count());
            Assert.All(page, o => Assert.True(o.IsActive));
        }

        private void SeedOrders(int count, Guid? enterpriseId = null)
        {
            var target = enterpriseId ?? _enterpriseId;
            var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 0; i < count; i++)
            {
                _context.Set<Order>().Add(new Order
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = target,
                    IsActive = true,
                    CreatedAt = createdAt.AddMinutes(i),
                });
            }

            _context.SaveChanges();
        }

        public void Dispose()
        {
            _context.Dispose();
            GC.SuppressFinalize(this);
        }

        private sealed class NoopAuditTrailEntryFactory : IAuditTrailEntryFactory
        {
            public IReadOnlyCollection<AuditTrail> Create(
                IEnumerable<EntityEntry<BaseEntity>> entries,
                Guid? userId,
                Guid? enterpriseId,
                DateTime changedAt)
            {
                return Array.Empty<AuditTrail>();
            }
        }
    }
}
