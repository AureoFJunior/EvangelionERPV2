﻿namespace EvangelionERPV2.Domain.Exceptions
{
    public class NotFoundDatabaseException : Exception
    {
        public NotFoundDatabaseException()
        {
        }

        public NotFoundDatabaseException(string message)
            : base(message)
        {
        }

        public NotFoundDatabaseException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}