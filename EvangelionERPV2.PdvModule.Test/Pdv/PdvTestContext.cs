using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.Shared.Auditing;
using System.Globalization;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// A real SQL database for the PDV tests, shared by every <see cref="AppDbContext"/> handed out
    /// by <see cref="CreateContext"/>.
    /// </summary>
    internal sealed class PdvTestContext : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _keepAlive;
        private readonly List<AppDbContext> _contexts = [];

        public PdvTestContext()
        {
            _connectionString = $"Data Source=pdv-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();

            using var schema = CreateContext();

            foreach (var type in new[]
            {
                typeof(Enterprise), typeof(Product), typeof(Order), typeof(OrderedProduct),
                typeof(CashSession), typeof(CashMovement), typeof(PdvSale), typeof(PdvSalePayment),
                typeof(PdvCharge), typeof(PdvTerminal), typeof(PdvOverrideAudit)
            })
            {
                CreateTable(schema, type);
            }
        }

        /// <summary>Creates one table and its unique indexes from EF's own metadata.</summary>
        private static void CreateTable(AppDbContext context, Type clrType)
        {
            var entityType = context.Model.FindEntityType(clrType)!;
            var columns = entityType.GetProperties()
                .Where(property => property.GetComputedColumnSql() == null)
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

            var table = entityType.GetTableName();

#pragma warning disable EF1002
            context.Database.ExecuteSqlRaw($"CREATE TABLE \"{table}\" ({string.Join(", ", columns)}, PRIMARY KEY(\"Id\"))");

            var index = 0;
            foreach (var entityIndex in entityType.GetIndexes().Where(item => item.IsUnique))
            {
                var names = string.Join(", ", entityIndex.Properties.Select(property => $"\"{property.Name}\""));

                if (entityIndex.GetFilter() != null)
                    continue;

                context.Database.ExecuteSqlRaw($"CREATE UNIQUE INDEX \"UX_{table}_{index++}\" ON \"{table}\" ({names})");
            }
#pragma warning restore EF1002
        }

        public AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connectionString)
                .Options;

            var context = new AppDbContext(
                options,
                httpContextAccessor: null,
                auditTrailEntryFactory: new NoopAuditTrailEntryFactory());

            _contexts.Add(context);
            return context;
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
                IEnumerable<EntityEntry<BaseEntity>> entries,
                Guid? userId,
                Guid? enterpriseId,
                DateTime changedAt)
            {
                return Array.Empty<AuditTrail>();
            }
        }
    }

    /// <summary>
    /// A <see cref="StripePixProvider"/> for suites that never charge anything.
    /// </summary>
    internal static class PixProviderStubs
    {
        public static Mock<StripePixProvider> Mock(MockBehavior behavior = MockBehavior.Loose) =>
            new(behavior,
                new AWSKMSKeyProvider(
                    new Mock<Amazon.SecretsManager.IAmazonSecretsManager>().Object,
                    new ConfigurationBuilder().AddInMemoryCollection().Build()),
                new ConfigurationBuilder().AddInMemoryCollection().Build());

        public static StripePixProvider Object() => Mock().Object;
    }
}
