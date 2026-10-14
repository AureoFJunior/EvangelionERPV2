using System.Globalization;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.StorefrontModule.Test
{
    /// <summary>A real SQL database shared by every <see cref="AppDbContext"/> it hands out.</summary>
    internal sealed class StorefrontTestContext : IDisposable
    {
        public static readonly Guid PlatformEnterpriseId = Guid.Parse("0f0f0f0f-0000-4000-8000-000000000001");

        private readonly string _connectionString;
        private readonly SqliteConnection _keepAlive;
        private readonly List<AppDbContext> _contexts = [];

        public Mock<IProductService<Product>> ProductService { get; } = new();

        /// <summary>How many of the next order creations fail as a database timeout would.</summary>
        public int FailCreates { get; set; }

        /// <summary>The order creation (1 = the first) that fails once as a database timeout would; 0 for none.</summary>
        public int FailOnCall { get; set; }

        internal int CreateCalls { get; set; }

        /// <summary>Runs inside the next failing creation, before it throws — e.g. stock sold elsewhere meanwhile.</summary>
        public Action<AppDbContext>? DuringFailure { get; set; }

        public StorefrontTestContext()
        {
            _connectionString = $"Data Source=storefront-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();

            using var schema = CreateContext();
            foreach (var type in new[]
                     {
                         typeof(Enterprise), typeof(EnterpriseFiscalSettings), typeof(Product), typeof(Order), typeof(OrderedProduct),
                         typeof(Customer), typeof(StorefrontCheckout), typeof(NFeDocument), typeof(Notification), typeof(User), typeof(ProductVariantGroup), typeof(StorefrontEvent),
                         typeof(StorefrontCategory), typeof(StorefrontCategorySlugAlias), typeof(ProductPriceHistory), typeof(ProductImage)
                     })
                CreateTable(schema, type);
        }

        private static void CreateTable(AppDbContext context, Type clrType)
        {
            var entityType = context.Model.FindEntityType(clrType)!;
            // Computed columns (User.NormalizedActiveEmail) become plain ones: EF reads them back after an insert.
            var columns = entityType.GetProperties()
                .Select(property =>
                {
                    var suffix = property.GetDefaultValue() switch
                    {
                        string text => $" DEFAULT '{text.Replace("'", "''")}'",
                        bool flag => $" DEFAULT {(flag ? 1 : 0)}",
                        decimal or int or long or double or float or short or byte
                            => $" DEFAULT {Convert.ToString(property.GetDefaultValue(), CultureInfo.InvariantCulture)}",
                        _ => string.Empty
                    };

                    return $"\"{property.Name}\" {property.GetColumnType()}{suffix}";
                });

#pragma warning disable EF1002
            context.Database.ExecuteSqlRaw($"CREATE TABLE \"{entityType.GetTableName()}\" ({string.Join(", ", columns)}, PRIMARY KEY(\"Id\"))");
#pragma warning restore EF1002
        }

        public AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options;
            var context = new AppDbContext(options, httpContextAccessor: null, auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
            _contexts.Add(context);
            return context;
        }

        public StorefrontService CreateService() =>
            new(CreateContext(), ProductService.Object,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Storefront:PlatformEnterpriseId"] = PlatformEnterpriseId.ToString(),
                        ["Storefront:Installments:MonthlyRates:0"] = "2.49",
                        ["Storefront:Installments:MonthlyRates:1"] = "2.59"
                    })
                    .Build());

        public Mock<OrderModule.Application.Interface.IOrderService<Order>> Orders => _orders ??= FakeOrderService.Create(this);
        private Mock<OrderModule.Application.Interface.IOrderService<Order>>? _orders;

        public StorefrontCategoryService CreateCategoryService() => new(CreateContext());

        public StorefrontCheckoutService CreateCheckoutService(TimeProvider clock, bool simulate = true) =>
            new(CreateContext(), Orders.Object,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Storefront:SimulatePayments"] = simulate.ToString(),
                        ["Storefront:Installments:MinValue"] = "10",
                        ["Storefront:Installments:MonthlyRates:0"] = "2.49",
                        ["Storefront:Installments:MonthlyRates:1"] = "2.59",
                        ["Storefront:Installments:MonthlyRates:2"] = "2.69",
                        ["Storefront:PlatformEnterpriseId"] = PlatformEnterpriseId.ToString()
                    })
                    .Build(),
                clock);

        public async Task SeedAsync(params object[] entities)
        {
            using var context = CreateContext();
            context.AddRange(entities);
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            foreach (var context in _contexts)
                context.Dispose();

            _keepAlive.Dispose();
        }

        private sealed class NoopAuditTrailEntryFactory : IAuditTrailEntryFactory
        {
            public IReadOnlyCollection<AuditTrail> Create(
                IEnumerable<EntityEntry<BaseEntity>> entries, Guid? userId, Guid? enterpriseId, DateTime changedAt) => [];
        }
    }

    internal static class Build
    {
        public static Enterprise ListedShop(string slug, string name = "Loja", bool listed = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = $"{slug}@example.com",
            PhoneNumber = "(11) 3000-0000",
            IsActive = true,
            IsListedOnStorefront = listed,
            StorefrontSlug = slug,
            StorefrontDescription = "Descrição",
            StorefrontShippingFee = 20m,
            StorefrontHandlingDays = 2,
            StorefrontDeliveryDaysMin = 3,
            StorefrontDeliveryDaysMax = 7,
            StorefrontListedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            StripeConnectedAccountId = "acct_test"
        };

        public static EnterpriseFiscalSettings Fiscal(Enterprise shop) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = shop.Id,
            RazaoSocial = shop.Name + " Ltda.",
            Cnpj = "11222333000181",
            Municipio = "Curitiba",
            Uf = "PR",
            IsActive = true
        };

        public static Product Item(
            Enterprise shop, string name, decimal price, decimal stock = 5, bool published = true,
            Guid? group = null, string? label = null, int ageDays = 0, Guid? category = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Descrição do produto",
            DefaultValue = price,
            StorageQuantity = stock,
            EnterpriseId = shop.Id,
            IsActive = true,
            IsPublishedOnStorefront = published,
            VariantGroupId = group,
            VariantLabel = label,
            StorefrontCategoryId = category,
            CostPrice = 1m,
            Ncm = "12345678",
            CreatedAt = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc).AddDays(-ageDays)
        };

        public static StorefrontCategory Category(string slug, StorefrontCategory? parent = null, bool active = true) => new()
        {
            Id = Guid.NewGuid(),
            ParentId = parent?.Id,
            Name = slug,
            Slug = slug,
            IsActive = active
        };
    }
}
