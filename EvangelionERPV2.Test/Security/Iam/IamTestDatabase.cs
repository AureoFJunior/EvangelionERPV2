using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EvangelionERPV2.Test.Security.Iam
{
    /// <summary>Shared in-memory SQLite with the tables IAM touches; transactions and ExecuteUpdate work, unlike EF InMemory.</summary>
    public sealed class IamTestDatabase : IDisposable
    {
        private readonly string _connectionString = $"Data Source=iam-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        private readonly SqliteConnection _keepAlive;
        private readonly List<AppDbContext> _contexts = [];

        public IamTestDatabase()
        {
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
            using var schema = CreateContext();
            foreach (var type in new[] { typeof(Enterprise), typeof(User), typeof(UserPermissionGrant), typeof(AuditTrail) })
            {
                var entity = schema.Model.FindEntityType(type)!;
                var columns = entity.GetProperties()
                    .Select(p => p.GetComputedColumnSql() == null
                        ? $"\"{p.GetColumnName()}\" {p.GetColumnType()}{DefaultOf(p)}"
                        : $"\"{p.GetColumnName()}\" {p.GetColumnType()}");
                schema.Database.ExecuteSqlRaw($"CREATE TABLE \"{entity.GetTableName()}\" ({string.Join(", ", columns)}, PRIMARY KEY(\"{entity.FindPrimaryKey()!.Properties[0].GetColumnName()}\"))");
            }
        }

        private static string DefaultOf(Microsoft.EntityFrameworkCore.Metadata.IProperty property) => property.GetDefaultValue() switch
        {
            null => string.Empty,
            string text => $" DEFAULT '{text.Replace("'", "''")}'",
            bool flag => $" DEFAULT {(flag ? 1 : 0)}",
            Enum value => $" DEFAULT ({Convert.ToInt64(value)})",
            DateTime moment => $" DEFAULT '{moment:yyyy-MM-dd HH:mm:ss}'",
            Guid id => $" DEFAULT '{id}'",
            IFormattable number => $" DEFAULT ({number.ToString(null, System.Globalization.CultureInfo.InvariantCulture)})",
            var other => $" DEFAULT '{other}'"
        };

        public AppDbContext CreateContext()
        {
            var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options,
                httpContextAccessor: null,
                auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
            _contexts.Add(context);
            return context;
        }

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
            public IReadOnlyCollection<AuditTrail> Create(IEnumerable<EntityEntry<BaseEntity>> entries, Guid? userId, Guid? enterpriseId, DateTime changedAt) => [];
        }
    }
}
