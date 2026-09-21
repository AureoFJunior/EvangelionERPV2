using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EvangelionERPV2.ProductModule.Test
{
    /// <summary>
    /// A real SQL database for the stock tests, shared by every <see cref="AppDbContext"/> handed
    /// out by <see cref="CreateContext"/>.
    /// </summary>
    internal sealed class StockLedgerTestContext : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _keepAlive;
        private readonly List<AppDbContext> _contexts = [];

        public StockLedgerTestContext()
        {
            _connectionString = $"Data Source=stock-ledger-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();

            using var schema = CreateContext();
            CreateTable(schema, typeof(Product));
            CreateTable(schema, typeof(StockMovement));
            CreateTable(schema, typeof(Enterprise));
        }

        /// <summary>
        /// Creates one table from EF's own metadata.
        /// </summary>
        private static void CreateTable(AppDbContext context, Type clrType)
        {
            var entityType = context.Model.FindEntityType(clrType)!;
            var columns = entityType.GetProperties()
                .Where(property => property.GetComputedColumnSql() == null)
                .Select(property => $"\"{property.Name}\" {property.GetColumnType()}");

            var ddl = $"CREATE TABLE \"{entityType.GetTableName()}\" ({string.Join(", ", columns)}, PRIMARY KEY(\"Id\"))";
            context.Database.ExecuteSqlRaw(ddl);
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

        /// <summary>
        /// The audit trail is not what these tests are about, and AppDbContext refuses to save
        /// without a factory registered.
        /// </summary>
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
