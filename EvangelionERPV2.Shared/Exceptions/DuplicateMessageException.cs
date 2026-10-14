namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// A queue message that has already been handled was delivered again.
    /// </summary>
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
