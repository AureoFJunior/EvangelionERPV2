using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EvangelionERPV2.Shared.Auditing
{
    public interface IAuditTrailEntryFactory
    {
        IReadOnlyCollection<AuditTrail> Create(
            IEnumerable<EntityEntry<BaseEntity>> entries,
            Guid? userId,
            DateTime changedAt);
    }
}
