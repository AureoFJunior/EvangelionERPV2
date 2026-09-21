namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// The message broker would not accept a publish: it is down, the connection dropped, or the
    /// queue is at <c>x-max-length</c>. Maps to 503 rather than 500 because the request was valid
    /// and a retry will very likely succeed.
    /// </summary>
    public class MessageBrokerUnavailableException : Exception
    {
        public MessageBrokerUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
