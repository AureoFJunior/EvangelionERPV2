using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Security;
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
        public async Task AMachineToken_ReachesEveryTenant()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterMachineRequestFor(TenantA);
            using var context = harness.CreateContext();

            Assert.Equal(3, await context.Customer.CountAsync());
        }

        [Fact]
        public async Task TheStorefrontServiceAccount_ReachesEveryTenant()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterStorefrontRequestFor(TenantA);
            using var context = harness.CreateContext();

            Assert.Equal(3, (await context.Customer.ToListAsync()).Count);
            Assert.Equal(2, (await context.Enterprise.ToListAsync()).Count);
        }

        [Fact]
        public async Task ASweepRunning_DoesNotWidenThePersonWhoAsksNext()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterMachineRequestFor(TenantA);
            using (var sweep = harness.CreateContext())
            {
                Assert.Equal(3, await sweep.Customer.CountAsync());
            }

            harness.EnterRequestFor(TenantB);
            using var person = harness.CreateContext();

            Assert.Equal(["B-Customer"], (await person.Customer.ToListAsync()).Select(x => x.Name));
        }

        [Fact]
        public async Task IgnoreQueryFilters_StillReachesEveryTenant()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterRequestFor(TenantA);
            using var context = harness.CreateContext();

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

            Assert.True(
                unfiltered.Count == 0,
                $"Entities carrying EnterpriseId with no tenant query filter: {string.Join(", ", unfiltered)}");
        }

        [Fact]
        public async Task AChildWithNoEnterpriseId_IsScopedThroughItsParent()
        {
            var harness = new TenantHarness();
            await harness.SeedAsync();

            harness.EnterRequestFor(TenantA);
            using var context = harness.CreateContext();

            var bills = await context.Set<Bill>().ToListAsync();

            Assert.Equal(["A-Bill"], bills.Select(x => x.DocumentNumber));
        }

        [Fact]
        public void NoFilteredEntity_IsTheRequiredEndOfAnUnfilteredRelationship()
        {
            var harness = new TenantHarness();
            using var context = harness.CreateContext();

            var gaps = context.Model.GetEntityTypes()
                .Where(entityType => entityType.BaseType == null && HasQueryFilter(entityType))
                .SelectMany(principal => principal.GetReferencingForeignKeys())
                .Where(foreignKey => foreignKey.IsRequired && !HasQueryFilter(foreignKey.DeclaringEntityType))
                .Select(foreignKey =>
                    $"{foreignKey.PrincipalEntityType.ClrType.Name} -> {foreignKey.DeclaringEntityType.ClrType.Name}")
                .Distinct()
                .Order()
                .ToList();

            Assert.True(
                gaps.Count == 0,
                "Filtered principal with an unfiltered required dependent - EF warns 10622 and the "
                + $"dependent's DbSet is not tenant-scoped: {string.Join(", ", gaps)}");
        }

        private static bool HasQueryFilter(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
        {
#pragma warning disable EF9100
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

            /// <summary>The same request, plus the claim <c>TokenService</c> mints for a machine login.</summary>
            public void EnterMachineRequestFor(Guid enterpriseId) =>
                _accessor.HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                            new Claim(MachineClaims.ClientType, MachineClaims.SelfApiValue)
                        ], "TestAuth"))
                };

            public void EnterStorefrontRequestFor(Guid enterpriseId) =>
                _accessor.HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                            new Claim(MachineClaims.TokenScope, MachineClaims.StorefrontScopeValue)
                        ], "TestAuth"))
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

                var orderA = new Order { Id = Guid.NewGuid(), EnterpriseId = TenantA, IsActive = true };
                var orderB = new Order { Id = Guid.NewGuid(), EnterpriseId = TenantB, IsActive = true };
                context.Order.AddRange(orderA, orderB);

                context.Bill.AddRange(
                    new Bill { Id = Guid.NewGuid(), OrderId = orderA.Id, DocumentNumber = "A-Bill", IsActive = true },
                    new Bill { Id = Guid.NewGuid(), OrderId = orderB.Id, DocumentNumber = "B-Bill", IsActive = true });

                await context.SaveChangesAsync();
            }
        }
    }
}
