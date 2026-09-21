using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EvangelionERPV2.OrderModule.Test.Orders
{
    /// <summary>
    /// Guards the idempotency key itself.
    /// </summary>
    public class QueuedOrderIdempotencyTests
    {
        [Fact]
        public void ProcessedMessage_HasUniqueIndexOnHandlerAndMessageId()
        {
            using var context = NewContext();

            var entity = context.Model.FindEntityType(typeof(ProcessedMessage));
            Assert.NotNull(entity);

            var index = entity!.GetIndexes().SingleOrDefault(i =>
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ProcessedMessage.Handler), nameof(ProcessedMessage.MessageId) }));

            Assert.NotNull(index);
            Assert.True(index!.IsUnique, "Dropping uniqueness here re-opens duplicate orders on redelivery.");
        }

        /// <summary>
        /// A tenant query filter would hide the rows the duplicate check reads, which would make
        /// every replay look like a first delivery.
        /// </summary>
        [Fact]
        public void ProcessedMessage_HasNoQueryFilter()
        {
            using var context = NewContext();

            var entity = context.Model.FindEntityType(typeof(ProcessedMessage));
            Assert.NotNull(entity);
            Assert.Null(entity!.GetQueryFilter());
        }

        [Fact]
        public void RetentionWindow_OutlivesTheDeadLetterTtl()
        {
            Assert.True(
                Shared.Utils.ProcessedMessageRetentionWorker.RetentionWindow > TimeSpan.FromDays(30),
                "Retention must outlive the 30-day dead-letter TTL or a redrive can duplicate.");
        }

        private static AppDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"processed-message-tests-{Guid.NewGuid():N}")
                .Options;

            return new AppDbContext(options, httpContextAccessor: null, auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
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
