using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Auditing
{
    /// <summary>
    /// Adds an audit row for an event no entity change describes. It is saved by the next
    /// SaveChanges on the request's context, so it commits or rolls back with the change it records.
    /// </summary>
    public interface IAuditTrailWriter
    {
        void Add(AuditTrail entry);
    }

    public sealed class AuditTrailWriter : IAuditTrailWriter
    {
        private readonly AppDbContext _context;

        public AuditTrailWriter(AppDbContext context) => _context = context;

        public void Add(AuditTrail entry) => _context.AuditTrails.Add(entry);
    }
}
