using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Security
{
    public class CsvFormulaInjectionTests
    {
        [Theory]
        [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
        [InlineData("+1+1", "'+1+1")]
        [InlineData("-2+3", "'-2+3")]
        [InlineData("@SUM(A1)", "'@SUM(A1)")]
        [InlineData("\tcmd", "'\tcmd")]
        [InlineData("-12.50", "-12.50")]
        [InlineData("Maria", "Maria")]
        [InlineData("", "")]
        public void ACellASpreadsheetWouldRun_IsWrittenAsText(string field, string expected)
        {
            Assert.Equal(expected + "\r\n", Csv.Line(field));
        }
    }
}
