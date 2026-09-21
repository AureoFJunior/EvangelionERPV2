using EvangelionERPV2.BillsModule.Application.Services;
using System.Text;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// The parser is the money boundary: everything downstream compares amounts for exact equality,
    /// so a cent lost here becomes a reconciliation that never closes.
    /// </summary>
    public class OfxStatementParserTests
    {
        /// <summary>
        /// Shaped after what a Brazilian bank actually emits: OFX 1.x SGML with unclosed tags, a
        /// timezone suffix on every date, a negative amount for the debit, and a LEDGERBAL sitting
        /// next to an AVAILBAL that carries a different figure.
        /// </summary>
        private const string RealisticOfx = """
            OFXHEADER:100
            DATA:OFXSGML
            VERSION:102
            CHARSET:1252

            <OFX>
            <BANKMSGSRSV1>
            <STMTTRNRS>
            <STMTRS>
            <CURDEF>BRL
            <BANKACCTFROM>
            <BANKID>341
            <ACCTID>12345-6
            <ACCTTYPE>CHECKING
            </BANKACCTFROM>
            <BANKTRANLIST>
            <DTSTART>20260801000000[-3:BRT]
            <DTEND>20260810235959[-3:BRT]
            <STMTTRN>
            <TRNTYPE>CREDIT
            <DTPOSTED>20260805120000[-3:BRT]
            <TRNAMT>1500.00
            <FITID>20260805001
            <MEMO>RECEBIMENTO PIX CLIENTE
            </STMTTRN>
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260807093000[-3:BRT]
            <TRNAMT>-249.90
            <FITID>20260807002
            <MEMO>PAGAMENTO FORNECEDOR
            </STMTTRN>
            </BANKTRANLIST>
            <LEDGERBAL>
            <BALAMT>10250.10
            <DTASOF>20260810235959[-3:BRT]
            </LEDGERBAL>
            <AVAILBAL>
            <BALAMT>9999.99
            <DTASOF>20260810235959[-3:BRT]
            </AVAILBAL>
            </STMTRS>
            </STMTTRNRS>
            </BANKMSGSRSV1>
            </OFX>
            """;

        [Fact]
        public void Parse_ReadsAccountBalanceAndTransactions()
        {
            var statement = OfxStatementParser.Parse(RealisticOfx);

            Assert.Equal("341", statement.BankId);
            Assert.Equal("12345-6", statement.AccountId);

            Assert.Equal(10250.10m, statement.LedgerBalance);
            Assert.Equal(new DateTime(2026, 8, 10), statement.BalanceAsOf);
            Assert.Equal(2, statement.Transactions.Count);
        }

        [Fact]
        public void Parse_KeepsTheSignOfADebit()
        {
            var debit = OfxStatementParser.Parse(RealisticOfx).Transactions.Single(x => x.FitId == "20260807002");

            Assert.Equal(-249.90m, debit.Amount);
            Assert.Equal("DEBIT", debit.TrnType);
            Assert.Equal("PAGAMENTO FORNECEDOR", debit.Memo);
        }

        [Fact]
        public void Parse_DropsTheClockAndTimezoneFromADate()
        {
            var credit = OfxStatementParser.Parse(RealisticOfx).Transactions.Single(x => x.FitId == "20260805001");

            Assert.Equal(new DateTime(2026, 8, 5), credit.PostedAt);
            Assert.Equal(1500.00m, credit.Amount);
        }

        [Fact]
        public void Parse_ReadsTheClosedTagsOfOfx2()
        {
            var xml = """
                <?xml version="1.0"?>
                <OFX><BANKACCTFROM><BANKID>033</BANKID><ACCTID>99887</ACCTID></BANKACCTFROM>
                <STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20260901</DTPOSTED><TRNAMT>-42.50</TRNAMT><FITID>X1</FITID><MEMO>TARIFA</MEMO></STMTTRN>
                <LEDGERBAL><BALAMT>100.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>
                """;

            var statement = OfxStatementParser.Parse(xml);

            Assert.Equal("033", statement.BankId);
            Assert.Equal(-42.50m, statement.Transactions.Single().Amount);
            Assert.Equal("TARIFA", statement.Transactions.Single().Memo);
        }

        [Theory]
        [InlineData("1234.56", "1234.56")]
        [InlineData("-1234.56", "-1234.56")]
        [InlineData("1.234,56", "1234.56")]
        [InlineData("-1.234,56", "-1234.56")]
        [InlineData("1234,56", "1234.56")]
        [InlineData("1,234.56", "1234.56")]
        public void Parse_ReadsLocaleFormattedAmounts(string raw, string expected)
        {
            var ofx = $"<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>{raw}</TRNAMT><FITID>F</FITID></STMTTRN>"
                + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>";

            Assert.Equal(
                decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                OfxStatementParser.Parse(ofx).Transactions.Single().Amount);
        }

        [Fact]
        public void Parse_DecodesCp1252WithoutManglingAccents()
        {
            var bytes = Encoding.Latin1.GetBytes(
                "<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>-10.00</TRNAMT>"
                + "<FITID>F</FITID><MEMO>PAGAMENTO NUTRIÇÃO</MEMO></STMTTRN>"
                + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>");

            Assert.Equal("PAGAMENTO NUTRIÇÃO", OfxStatementParser.Parse(bytes).Transactions.Single().Memo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not an ofx file at all")]
        [InlineData("<OFX><ACCTID>1</ACCTID></OFX>")]
        [InlineData("<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>-10.00</TRNAMT></STMTTRN>"
            + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>")]
        [InlineData("<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>abc</TRNAMT><FITID>F</FITID></STMTTRN>"
            + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>")]
        public void Parse_RefusesWhatItCannotFullyRead(string text)
        {
            Assert.Throws<FormatException>(() => OfxStatementParser.Parse(text));
        }
    }
}
