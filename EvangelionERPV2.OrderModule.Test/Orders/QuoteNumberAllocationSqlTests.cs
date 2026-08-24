using EvangelionERPV2.OrderModule.Domain.Repositories;

namespace EvangelionERPV2.OrderModule.Test
{
    /// <summary>
    /// Pins the shape of the quote-number allocation statement.
    /// </summary>
    /// <remarks>
    /// Every clause is load-bearing for collision safety and this is the one piece with no
    /// integration test behind it, so the text is asserted instead — exactly as
    /// <c>BillNumberAllocationSqlTests</c> does for the boleto counter.
    /// </remarks>
    public class QuoteNumberAllocationSqlTests
    {
        [Fact]
        public void AllocateNextNumberSql_IsOneAtomicUpdateReturningTheOldValue()
        {
            var sql = QuoteRepository.AllocateNextNumberSql;

            // UPDATE ... OUTPUT deleted is atomic at any isolation level: two salespeople saving at
            // the same moment cannot be handed the same number.
            Assert.Contains("UPDATE dbo.QuoteNumberSequence", sql);
            Assert.Contains("OUTPUT deleted.NextNumber AS Value", sql);
            Assert.Contains("SET NextNumber = NextNumber + 1", sql);

            // ROWLOCK keeps the engine from escalating to a page lock and serialising tenants.
            Assert.Contains("WITH (ROWLOCK)", sql);

            // Scoped to the tenant, and parameterised — never interpolated.
            Assert.Contains("WHERE EnterpriseId = @enterpriseId", sql);

            // One statement. A SELECT-then-UPDATE would race exactly where this must not.
            Assert.DoesNotContain("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        }
    }
}
