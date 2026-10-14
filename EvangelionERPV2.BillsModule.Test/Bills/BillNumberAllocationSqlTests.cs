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
            Assert.Contains("UPDATE dbo.BillNumberSequence", Sql, StringComparison.Ordinal);
            Assert.Contains("SET NextNumber = NextNumber + 1", Sql, StringComparison.Ordinal);
            Assert.Contains("OUTPUT deleted.NextNumber", Sql, StringComparison.Ordinal);

            Assert.DoesNotContain("SELECT", Sql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AllocationSql_IsASingleStatement()
        {
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
            Assert.Contains("NextNumber <= Ceiling", Sql, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("@enterpriseId")]
        [InlineData("@bankCode")]
        [InlineData("@wallet")]
        public void AllocationSql_ScopesToTheFullSequenceKeyViaParameters(string parameterName)
        {
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
