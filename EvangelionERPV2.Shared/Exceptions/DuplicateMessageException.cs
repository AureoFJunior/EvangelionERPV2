namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// A queue message that has already been handled was delivered again.
    /// </summary>
    /// <remarks>
    /// Not a failure. Delivery is at-least-once, so a redelivery after a dropped response is the
    /// system working as designed; the duplicate work is what has to be refused. Callers treat this
    /// as success and acknowledge the message, because retrying would only produce it again.
    /// </remarks>
    public class DuplicateMessageException : Exception
    {
        public DuplicateMessageException(string messageId)
            : base($"Message '{messageId}' has already been processed.")
        {
            MessageId = messageId;
        }

        public string MessageId { get; }
    }
}
