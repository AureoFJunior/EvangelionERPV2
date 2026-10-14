namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// An order that would put a customer past the credit limit set for them.
    /// </summary>
    public class CreditLimitExceededException : Exception
    {
        public CreditLimitExceededException(string message) : base(message)
        {
        }
    }
}
