using EvangelionERPV2.BillsModule.Domain.Repositories;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// NossoNumero allocation is the safety-critical half of the boleto: two instances handed the
    /// same number means two titles the bank cannot tell apart, which rejects one or credits the
    /// wrong one. A concurrency test needs a real SQL Server, which this suite does not have, so
    /// the same alternative NFeNumberAllocationSqlTests uses applies — assert the emitted SQL text.
    /// Every clause below is load-bearing; none of them is style.
    /// </summary>
    public class BillNumberAllocationSqlTests
    {
        private static readonly string Sql = BillsRepository.AllocateNextNumberSql;

        [Fact]
        public void AllocationSql_ReadsAndIncrementsInOneAtomicStatement()
        {
            // OUTPUT deleted.* returns the pre-increment value from within the UPDATE itself. A
            // SELECT-then-UPDATE would be a lost-update race no isolation level below SERIALIZABLE
            // prevents.
            Assert.Contains("UPDATE dbo.BillNumberSequence", Sql, StringComparison.Ordinal);
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
        public void AllocationSql_TakesARowLockSoUnrelatedWalletsDoNotSerialise()
        {
            Assert.Contains("WITH (ROWLOCK)", Sql, StringComparison.Ordinal);
        }

        [Fact]
        public void AllocationSql_StopsAtTheContractedCeilingInsideTheSameStatement()
        {
            // A number past the convênio's range is one the bank will not honour. Checking it in
            // the WHERE keeps it atomic with the increment; a separate read could pass the check
            // and then lose the race to another instance's allocation.
            Assert.Contains("NextNumber <= Ceiling", Sql, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("@enterpriseId")]
        [InlineData("@bankCode")]
        [InlineData("@wallet")]
        public void AllocationSql_ScopesToTheFullSequenceKeyViaParameters(string parameterName)
        {
            // All three are required: the unique index is on the tuple, so dropping one would let a
            // counter be shared across tenants, banks or wallets — each of which has its own range.
            // Parameterised, never interpolated: the wallet is operator-supplied text.
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
