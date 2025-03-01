namespace EvangelionERPV2.Shared.Exceptions
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
