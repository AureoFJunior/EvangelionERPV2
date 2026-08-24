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
    /// <remarks>
    /// EFCore.InMemory cannot execute SQL, and the whole point of the stock path is a conditional
    /// UPDATE — testing it against a provider that ignores SQL would only test the mock. SQLite
    /// runs the statement for real, including the "affected rows = 0" outcome that is the
    /// oversell guard.
    ///
    /// One shared in-memory database behind several connections is what lets two contexts race
    /// for the same row the way two requests would.
    /// </remarks>
    internal sealed class StockLedgerTestContext : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _keepAlive;
        private readonly List<AppDbContext> _contexts = [];

        public StockLedgerTestContext()
        {
            // The shared in-memory database lives exactly as long as one connection to it stays
            // open, so this one is held for the fixture's lifetime and never used for queries.
            _connectionString = $"Data Source=stock-ledger-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();

            using var schema = CreateContext();
            CreateTable(schema, typeof(Product));
            CreateTable(schema, typeof(StockMovement));
            // The stock ledger reads Enterprise.ClosedThroughUtc before it writes: a movement into
            // a closed period is refused. No row means no lock, which is what these tests assume.
            CreateTable(schema, typeof(Enterprise));
        }

        /// <summary>
        /// Creates one table from EF's own metadata.
        /// </summary>
        /// <remarks>
        /// <c>EnsureCreated()</c> cannot be used: the model carries SQL Server computed columns
        /// (<c>CONVERT(nvarchar(450), ...)</c> on User) that SQLite refuses to parse, and it would
        /// build twenty-odd tables these tests never read anyway. Deriving the DDL from
        /// <c>context.Model</c> instead of hand-writing it means a new column on Product does not
        /// silently fall out of the test schema.
        /// </remarks>
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
