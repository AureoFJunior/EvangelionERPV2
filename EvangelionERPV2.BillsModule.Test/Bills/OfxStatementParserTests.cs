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

            // The LEDGERBAL figure, not the AVAILBAL one sitting right after it.
            Assert.Equal(10250.10m, statement.LedgerBalance);
            Assert.Equal(new DateTime(2026, 8, 10), statement.BalanceAsOf);
            Assert.Equal(2, statement.Transactions.Count);
        }

        [Fact]
        public void Parse_KeepsTheSignOfADebit()
        {
            var debit = OfxStatementParser.Parse(RealisticOfx).Transactions.Single(x => x.FitId == "20260807002");

            // Negative survives: the sign is what tells the matcher to look at payables, not orders.
            Assert.Equal(-249.90m, debit.Amount);
            Assert.Equal("DEBIT", debit.TrnType);
            Assert.Equal("PAGAMENTO FORNECEDOR", debit.Memo);
        }

        [Fact]
        public void Parse_DropsTheClockAndTimezoneFromADate()
        {
            var credit = OfxStatementParser.Parse(RealisticOfx).Transactions.Single(x => x.FitId == "20260805001");

            // "20260805120000[-3:BRT]" is the shape every real file carries. Honouring the offset
            // would let a late-evening transaction land on the previous day on a UTC host and shift
            // it a day inside the ±3-day window.
            Assert.Equal(new DateTime(2026, 8, 5), credit.PostedAt);
            Assert.Equal(1500.00m, credit.Amount);
        }

        [Fact]
        public void Parse_ReadsTheClosedTagsOfOfx2()
        {
            // OFX 2.0 is real XML. The same tag regex has to read it, because which dialect a bank
            // emits is not something the uploader knows or should have to declare.
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
        // The spec's own format.
        [InlineData("1234.56", "1234.56")]
        [InlineData("-1234.56", "-1234.56")]
        // What Brazilian banks ship regardless of the spec.
        [InlineData("1.234,56", "1234.56")]
        [InlineData("-1.234,56", "-1234.56")]
        [InlineData("1234,56", "1234.56")]
        // Grouped thousands with a dot decimal.
        [InlineData("1,234.56", "1234.56")]
        public void Parse_ReadsLocaleFormattedAmounts(string raw, string expected)
        {
            var ofx = $"<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>{raw}</TRNAMT><FITID>F</FITID></STMTTRN>"
                + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>";

            // Expectation carried as a string and parsed to decimal: routing it through a double
            // literal would be the exact bug the decimal-only rule exists to prevent.
            Assert.Equal(
                decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                OfxStatementParser.Parse(ofx).Transactions.Single().Amount);
        }

        [Fact]
        public void Parse_DecodesCp1252WithoutManglingAccents()
        {
            // Brazilian banks still emit cp1252. Read as UTF-8 with replacement, "PAGAMENTO NUTRIÇÃO"
            // comes back full of replacement characters in the one field a human reads to clear a line.
            var bytes = Encoding.Latin1.GetBytes(
                "<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>-10.00</TRNAMT>"
                + "<FITID>F</FITID><MEMO>PAGAMENTO NUTRIÇÃO</MEMO></STMTTRN>"
                + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>");

            Assert.Equal("PAGAMENTO NUTRIÇÃO", OfxStatementParser.Parse(bytes).Transactions.Single().Memo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not an ofx file at all")]
        // No LEDGERBAL: there is nothing to compute a variance against, so this is not a partial
        // success. Importing the movement anyway would leave the screen showing a stale balance.
        [InlineData("<OFX><ACCTID>1</ACCTID></OFX>")]
        // A transaction with no FITID has no idempotency key, so a re-import would duplicate it.
        [InlineData("<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>-10.00</TRNAMT></STMTTRN>"
            + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>")]
        // An unreadable amount must never silently become zero.
        [InlineData("<OFX><ACCTID>1</ACCTID><STMTTRN><DTPOSTED>20260901</DTPOSTED><TRNAMT>abc</TRNAMT><FITID>F</FITID></STMTTRN>"
            + "<LEDGERBAL><BALAMT>0.00</BALAMT><DTASOF>20260901</DTASOF></LEDGERBAL></OFX>")]
        public void Parse_RefusesWhatItCannotFullyRead(string text)
        {
            Assert.Throws<FormatException>(() => OfxStatementParser.Parse(text));
        }
    }
}
