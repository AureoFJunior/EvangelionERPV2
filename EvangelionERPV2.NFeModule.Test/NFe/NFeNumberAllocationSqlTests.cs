using EvangelionERPV2.NFeModule.Domain.Repositories;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// Number allocation is the safety-critical half of the duplicate-emission mechanism: if two
    /// instances can be handed the same nNF, a tenant emits two notes on one number and that is a
    /// real tax liability. A concurrency test needs a real SQL Server, which this suite does not
    /// have, so the spec's stated alternative applies — assert the emitted SQL text.
    /// Every clause below is load-bearing; none of them is style.
    /// </summary>
    public class NFeNumberAllocationSqlTests
    {
        private static readonly string Sql = NFeRepository.AllocateNextNumberSql;

        [Fact]
        public void AllocationSql_ReadsAndIncrementsInOneAtomicStatement()
        {
            // OUTPUT deleted.* returns the pre-increment value from within the UPDATE itself. A
            // SELECT-then-UPDATE would be a lost-update race no isolation level below SERIALIZABLE
            // prevents.
            Assert.Contains("UPDATE dbo.NFeNumberSequence", Sql, StringComparison.Ordinal);
            Assert.Contains("SET NextNumber = NextNumber + 1", Sql, StringComparison.Ordinal);
            Assert.Contains("OUTPUT deleted.NextNumber", Sql, StringComparison.Ordinal);

            Assert.DoesNotContain("SELECT", Sql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AllocationSql_IsASingleStatement()
        {
            // Two statements would no longer be atomic without an explicit transaction.
            Assert.Equal(1, Sql.Count(character => character == ';'));
            Assert.EndsWith(";", Sql.TrimEnd(), StringComparison.Ordinal);
        }

        [Fact]
        public void AllocationSql_TakesARowLockSoUnrelatedSeriesDoNotSerialise()
        {
            Assert.Contains("WITH (ROWLOCK)", Sql, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("@enterpriseId")]
        [InlineData("@model")]
        [InlineData("@series")]
        [InlineData("@environment")]
        public void AllocationSql_ScopesToTheFullSequenceKeyViaParameters(string parameterName)
        {
            // All four are required: the unique index is on the tuple, so dropping one would let a
            // counter be shared across tenants, models or environments. Parameterised, never
            // interpolated — this statement takes a tenant-supplied series string.
            Assert.Contains(parameterName, Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void AllocationSql_DoesNotInterpolateValues()
        {
            Assert.DoesNotContain("'", Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("{", Sql, StringComparison.Ordinal);
        }
    }
}
