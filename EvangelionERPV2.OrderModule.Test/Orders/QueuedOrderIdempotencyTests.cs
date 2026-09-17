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
    /// <remarks>
    /// Measured 2026-09-17 against the running system: replaying one order message created a second
    /// order and decremented stock twice, because <c>CreateAsync</c> mints a new <c>Order.Id</c> on
    /// every call. The unique index on (Handler, MessageId) is what makes the replay a no-op, so a
    /// change that drops it silently restores duplicate orders on any dropped HTTP response.
    ///
    /// The in-memory provider does not enforce unique indexes, so these assert the model rather than
    /// the database behaviour. The constraint is verified end to end against SQL Server.
    /// </remarks>
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
            // The DLQ holds a message for 30 days, and a redrive replays it. A retention window
            // shorter than that would forget the key while the message it guards is still alive.
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
