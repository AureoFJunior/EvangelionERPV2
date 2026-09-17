namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One row per queue message that has been fully handled, keyed by the broker's
    /// <c>MessageId</c>.
    /// </summary>
    /// <remarks>
    /// Delivery is at-least-once: the message is acked only after the handler returns, so a handler
    /// that succeeds and then fails to report success — a dropped HTTP response, a worker killed
    /// mid-flight — is redelivered. Measured 2026-09-17: replaying one order message created a
    /// second order and decremented stock twice, because <c>CreateAsync</c> mints a new
    /// <c>Order.Id</c> on every call.
    ///
    /// The unique index on <see cref="MessageId"/> is the part that matters, not the lookup. Two
    /// concurrent deliveries of the same message both see an empty table; one commits and the other
    /// violates the constraint, which is how the race is settled by the database rather than by
    /// timing. Written inside the caller's transaction so the row and the work it guards commit or
    /// roll back together.
    ///
    /// Not a <c>BaseEntity</c>: there is no tenant on it and no soft delete. A row here is a fact
    /// about the broker, not tenant data, and a global query filter would hide exactly the rows the
    /// check exists to find.
    /// </remarks>
    public class ProcessedMessage
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The broker's message id, preserved across retries and redrives — the same value survives
        /// every replay of the same work, which is what makes it usable as the key.
        /// </summary>
        public string MessageId { get; set; } = string.Empty;

        /// <summary>
        /// Which pipeline handled it, so two handlers consuming the same message are not mistaken
        /// for a duplicate of each other.
        /// </summary>
        public string Handler { get; set; } = string.Empty;

        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
