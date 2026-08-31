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
    /// <remarks>
    /// Same reasoning as the stock ledger's harness, plus one addition that matters here: the
    /// unique indexes are created too. The idempotency claim <em>is</em> the index — without it a
    /// test would pass while two concurrent syncs both created an order for the same basket, which
    /// is the exact failure this module exists to prevent.
    /// </remarks>
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
                typeof(PdvCharge)
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
                    // The DEFAULT matters. EF omits a property whose value is the CLR default from
                    // the INSERT and reads it back instead, so a column declared here without the
                    // default its model carries (Enterprise.Currency, Enterprise.CurrentBalance)
                    // comes back NULL and the save dies in the reader rather than on an assertion.
                    var suffix = property.GetDefaultValue() switch
                    {
                        string text => $" DEFAULT '{text.Replace("'", "''")}'",
                        bool flag => $" DEFAULT {(flag ? 1 : 0)}",
                        decimal or int or long or double or float or short or byte
                            => $" DEFAULT {Convert.ToString(property.GetDefaultValue(), CultureInfo.InvariantCulture)}",

                        // Anything else (a DateTime, an enum) is left to EF's read-back. No test
                        // here seeds one, and guessing its SQLite literal is how the last attempt
                        // produced "near '-': syntax error" out of DateTime.MinValue.
                        _ => string.Empty
                    };

                    return $"\"{property.Name}\" {property.GetColumnType()}{suffix}";
                });

            var table = entityType.GetTableName();
            context.Database.ExecuteSqlRaw($"CREATE TABLE \"{table}\" ({string.Join(", ", columns)}, PRIMARY KEY(\"Id\"))");

            var index = 0;
            foreach (var entityIndex in entityType.GetIndexes().Where(item => item.IsUnique))
            {
                var names = string.Join(", ", entityIndex.Properties.Select(property => $"\"{property.Name}\""));

                // SQLite has no filtered-index syntax matching SQL Server's, and the filter on the
                // session index ("one OPEN session per terminal") is not what these tests assert.
                // The client-key indexes carry no filter, so they translate exactly.
                if (entityIndex.GetFilter() != null)
                    continue;

                context.Database.ExecuteSqlRaw($"CREATE UNIQUE INDEX \"UX_{table}_{index++}\" ON \"{table}\" ({names})");
            }
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
    /// <remarks>
    /// Its methods are virtual, so Moq stands in for them without a hand-rolled interface around
    /// Stripe.net. Nothing here reaches a network: the KMS provider is handed a mock secrets client
    /// and is never asked for a key, because no test that uses this stub calls the provider at all.
    /// </remarks>
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
