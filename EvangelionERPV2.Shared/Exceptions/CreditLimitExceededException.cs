namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// An order that would put a customer past the credit limit set for them.
    /// </summary>
    /// <remarks>
    /// Like <see cref="PeriodClosedException"/> this is not an <see cref="ArgumentException"/>: the
    /// request is well formed and the same order would have been accepted before the customer's
    /// other invoices went unpaid. It is the state of the book that refuses it, which is a 409.
    /// Whoever holds customers.credit.override can send the same order again and have it taken.
    /// </remarks>
    public class CreditLimitExceededException : Exception
    {
        public CreditLimitExceededException(string message) : base(message)
        {
        }
    }
}
