using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Tenant isolation now lives in the model, not in sixteen controllers that each have to
    /// remember. These tests are the proof, and the third one is the reason they exist at all: EF
    /// caches the model, so the query filters close over whichever context instance happened to
    /// build it. If the ambient tenant were read from that instance's own state, every request
    /// after the first would be answered with the first request's tenant — a cross-tenant leak that
    /// no controller test would ever catch.
    /// </summary>
    public class TenantQueryFilterTests
    {
        private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [Fact]
        public async Task Query_InsideARequest_SeesOnlyItsOwnTenant()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterRequestFor(TenantA);
            using var context = harness.CreateContext();

            var customers = await context.Customer.ToListAsync();

            Assert.Equal(["A-Customer"], customers.Select(x => x.Name));
        }

        [Fact]
        public async Task Query_OutsideARequest_IsNotFiltered()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            // No HttpContext: a hosted service, a worker, or the login lookup that runs before any
            // tenant is known. These are legitimately cross-tenant and must keep working.
            harness.LeaveRequest();
            using var context = harness.CreateContext();

            var customers = await context.Customer.ToListAsync();

            Assert.Equal(3, customers.Count);
        }

        [Fact]
        public async Task Filters_FollowTheCurrentRequest_NotTheContextThatBuiltTheModel()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            // The first context to run a query is the one whose instance the cached model's filters
            // captured.
            harness.EnterRequestFor(TenantA);
            using (var first = harness.CreateContext())
            {
                Assert.Equal(["A-Customer"], (await first.Customer.ToListAsync()).Select(x => x.Name));
            }

            harness.EnterRequestFor(TenantB);
            using var second = harness.CreateContext();

            Assert.Equal(["B-Customer"], (await second.Customer.ToListAsync()).Select(x => x.Name));
        }

        [Fact]
        public async Task Query_InsideARequest_HidesRowsBelongingToNobody()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterRequestFor(TenantA);
            using var context = harness.CreateContext();

            // The legacy NULL-tenant row. Every hand-written controller filter compares for
            // equality, so this row was already invisible through the API; the model filter must not
            // quietly start surfacing it to everyone.
            Assert.DoesNotContain(await context.Customer.ToListAsync(), x => x.EnterpriseId == null);
        }

        [Fact]
        public async Task Enterprise_IsScopedByItsOwnKey()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterRequestFor(TenantB);
            using var context = harness.CreateContext();

            var enterprises = await context.Enterprise.ToListAsync();

            Assert.Equal([TenantB], enterprises.Select(x => x.Id));
        }

        [Fact]
        public async Task IgnoreQueryFilters_StillReachesEveryTenant()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterRequestFor(TenantA);
            using var context = harness.CreateContext();

            // The deliberate escape hatch: cross-tenant maintenance work inside a request has to
            // stay possible, and has to be spelled out at the call site where a reviewer sees it.
            Assert.Equal(3, await context.Customer.IgnoreQueryFilters().CountAsync());
        }

        [Fact]
        public void EveryTenantOwnedEntity_HasATenantFilter()
        {
            var harness = new TenantHarness();
            using var context = harness.CreateContext();

            var unfiltered = context.Model.GetEntityTypes()
                .Where(entityType => entityType.BaseType == null)
                .Where(entityType => entityType.ClrType.GetProperty("EnterpriseId") != null)
                .Where(entityType => !HasQueryFilter(entityType))
                .Select(entityType => entityType.ClrType.Name)
                .Order()
                .ToList();

            // Structural on purpose: a module added later inherits isolation from the convention, and
            // this fails if anything ever removes it for one entity.
            Assert.True(
                unfiltered.Count == 0,
                $"Entities carrying EnterpriseId with no tenant query filter: {string.Join(", ", unfiltered)}");
        }

        private static bool HasQueryFilter(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
        {
#pragma warning disable EF9100 // Named query filters are still experimental in EF 10; the single-filter accessor is what this model uses.
            return entityType.GetDeclaredQueryFilters().Any();
#pragma warning restore EF9100
        }

        /// <summary>
        /// One in-memory database and one accessor whose HttpContext is swapped between "requests",
        /// which is what makes the model-cache hazard observable at all.
        /// </summary>
        private sealed class TenantHarness
        {
            private readonly DbContextOptions<AppDbContext> _options;
            private readonly HttpContextAccessor _accessor = new();

            public TenantHarness()
            {
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"tenant-filters-{Guid.NewGuid()}")
                    .Options;
            }

            public AppDbContext CreateContext() => new(_options, _accessor, new AuditTrailEntryFactory());

            public void EnterRequestFor(Guid enterpriseId) =>
                _accessor.HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())], "TestAuth"))
                };

            public void LeaveRequest() => _accessor.HttpContext = null;

            public async Task SeedAsync()
            {
                LeaveRequest();
                using var context = CreateContext();

                context.Enterprise.AddRange(
                    new Enterprise { Id = TenantA, Name = "A", IsActive = true },
                    new Enterprise { Id = TenantB, Name = "B", IsActive = true });

                context.Customer.AddRange(
                    new Customer { Id = Guid.NewGuid(), Name = "A-Customer", EnterpriseId = TenantA, IsActive = true },
                    new Customer { Id = Guid.NewGuid(), Name = "B-Customer", EnterpriseId = TenantB, IsActive = true },
                    new Customer { Id = Guid.NewGuid(), Name = "Orphan", EnterpriseId = null, IsActive = true });

                await context.SaveChangesAsync();
            }
        }
    }
}
