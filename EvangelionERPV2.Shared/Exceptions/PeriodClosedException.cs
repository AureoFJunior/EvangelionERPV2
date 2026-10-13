namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// A write whose business date falls inside a period the tenant has closed.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <see cref="ArgumentException"/>: the request is well formed and would
    /// have succeeded yesterday. It is the state of the books that forbids it, which is a 409 —
    /// the API host's exception middleware maps it there.
    /// </remarks>
    public class PeriodClosedException : Exception
    {
        public PeriodClosedException(string message) : base(message)
        {
        }
    }
}
