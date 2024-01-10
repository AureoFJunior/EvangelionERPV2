namespace EvangelionERPV2.Domain.Utils
{
    public class InsertDatabaseException : Exception
    {
        public InsertDatabaseException()
        {
        }

        public InsertDatabaseException(string message)
            : base(message)
        {
        }

        public InsertDatabaseException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
