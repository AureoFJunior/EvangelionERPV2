namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One row per queue message that has been fully handled, keyed by the broker's
    /// <c>MessageId</c>.
    /// </summary>
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
