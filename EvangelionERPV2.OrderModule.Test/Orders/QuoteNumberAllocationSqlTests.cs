using EvangelionERPV2.OrderModule.Domain.Repositories;

namespace EvangelionERPV2.OrderModule.Test
{
    /// <summary>
    /// Pins the shape of the quote-number allocation statement.
    /// </summary>
    public class QuoteNumberAllocationSqlTests
    {
        [Fact]
        public void AllocateNextNumberSql_IsOneAtomicUpdateReturningTheOldValue()
        {
            var sql = QuoteRepository.AllocateNextNumberSql;

            Assert.Contains("UPDATE dbo.QuoteNumberSequence", sql);
            Assert.Contains("OUTPUT deleted.NextNumber AS Value", sql);
            Assert.Contains("SET NextNumber = NextNumber + 1", sql);

            Assert.Contains("WITH (ROWLOCK)", sql);

            Assert.Contains("WHERE EnterpriseId = @enterpriseId", sql);

            Assert.DoesNotContain("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        }
    }
}
