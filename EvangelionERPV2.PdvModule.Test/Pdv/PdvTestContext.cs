using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
                typeof(CashSession), typeof(CashMovement), typeof(PdvSale), typeof(PdvSalePayment)
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
                .Select(property => $"\"{property.Name}\" {property.GetColumnType()}");

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
}
