namespace EvangelionERPV2.Domain.Exceptions
{
    public class EmailSenderException : Exception
    {
        public EmailSenderException()
        {
        }

        public EmailSenderException(string message)
            : base(message)
        {
        }

        public EmailSenderException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
