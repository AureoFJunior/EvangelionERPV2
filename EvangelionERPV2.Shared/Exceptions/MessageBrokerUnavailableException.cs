namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// The message broker would not accept a publish: it is down, the connection dropped, or the
    /// queue is at <c>x-max-length</c>. Maps to 503 rather than 500 because the request was valid
    /// and a retry will very likely succeed.
    /// </summary>
    /// <remarks>
    /// Safe to advertise as retryable only because the publish path performs no database write
    /// beforehand, so a retry cannot duplicate an order. That stops being true if a write ever moves
    /// ahead of the publish.
    /// </remarks>
    public class MessageBrokerUnavailableException : Exception
    {
        public MessageBrokerUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
