using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>One <c>STMTTRN</c> block.</summary>
    public sealed record OfxTransaction(
        string FitId,
        DateTime PostedAt,
        decimal Amount,
        string TrnType,
        string Memo);

    /// <summary>A parsed OFX statement: the account it covers, the bank's closing balance, and the movement.</summary>
    public sealed record OfxStatement(
        string BankId,
        string AccountId,
        decimal LedgerBalance,
        DateTime BalanceAsOf,
        IReadOnlyList<OfxTransaction> Transactions);

    /// <summary>
    /// Reads the transaction list out of an OFX statement.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than pulled from a package, and the narrowness is the argument: OFX 1.x
    /// is SGML with unclosed tags, so a conforming XML parser rejects it outright, and a general
    /// SGML library would be a dependency carried forever to read six tags. The regex below reads
    /// both dialects for free — <c>[^&lt;\r\n]*</c> stops at the closing tag of OFX 2.0 (XML) and at
    /// the end of line of OFX 1.x (SGML).
    ///
    /// Everything this cannot understand throws <see cref="FormatException"/>, which the controller
    /// turns into a 400. There is no partial-parse mode on purpose: half a statement reconciled
    /// against a full balance is a variance figure that lies.
    /// </remarks>
    public static class OfxStatementParser
    {
        // The file is uploaded, so the patterns run against untrusted input. All of them are
        // anchored on literal tags with no nested quantifier, but the timeout is the cheap belt.
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

        private static readonly Regex TransactionBlock =
            new(@"<STMTTRN>(.*?)</STMTTRN>", RegexOptions.Singleline | RegexOptions.IgnoreCase, MatchTimeout);

        private static readonly Regex LedgerBalanceBlock =
            new(@"<LEDGERBAL>(.*?)</LEDGERBAL>", RegexOptions.Singleline | RegexOptions.IgnoreCase, MatchTimeout);

        /// <summary>
        /// Decodes the uploaded bytes, then parses. Brazilian banks still emit cp1252, so a strict
        /// UTF-8 read is attempted first and Latin-1 catches the rest — decoding cp1252 as UTF-8
        /// with replacement would silently mangle every accented counterparty name in MEMO.
        /// </summary>
        public static OfxStatement Parse(byte[] content)
        {
            if (content == null || content.Length == 0)
                throw new FormatException("The statement file is empty.");

            string text;
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(content);
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.Latin1.GetString(content);
            }

            return Parse(text);
        }

        public static OfxStatement Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.Contains("<OFX", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("The file is not an OFX statement.");

            var ledgerBlock = LedgerBalanceBlock.Match(text);
            if (!ledgerBlock.Success)
                throw new FormatException("The statement carries no LEDGERBAL closing balance.");

            // Scoped to the LEDGERBAL block: AVAILBAL carries a BALAMT and a DTASOF of its own, and
            // the available balance is not the one the ERP balance should be compared against.
            var balance = ParseAmount(Required(ledgerBlock.Value, "BALAMT"));
            var balanceAsOf = ParseDate(Required(ledgerBlock.Value, "DTASOF"));

            var transactions = new List<OfxTransaction>();
            foreach (Match block in TransactionBlock.Matches(text))
            {
                transactions.Add(new OfxTransaction(
                    FitId: Required(block.Value, "FITID"),
                    PostedAt: ParseDate(Required(block.Value, "DTPOSTED")),
                    Amount: ParseAmount(Required(block.Value, "TRNAMT")),
                    TrnType: Tag(block.Value, "TRNTYPE") ?? string.Empty,
                    // NAME is what some banks fill instead of MEMO; either is the counterparty text.
                    Memo: Tag(block.Value, "MEMO") ?? Tag(block.Value, "NAME") ?? string.Empty));
            }

            return new OfxStatement(
                BankId: Tag(text, "BANKID") ?? string.Empty,
                AccountId: Required(text, "ACCTID"),
                LedgerBalance: balance,
                BalanceAsOf: balanceAsOf,
                Transactions: transactions);
        }

        private static string? Tag(string source, string tag)
        {
            var match = Regex.Match(source, $@"<{tag}>([^<\r\n]*)", RegexOptions.IgnoreCase, MatchTimeout);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string Required(string source, string tag)
        {
            var value = Tag(source, tag);

            if (string.IsNullOrEmpty(value))
                throw new FormatException($"The statement is missing a required {tag} value.");

            return value;
        }

        /// <summary>
        /// Money, as decimal. Never double: a statement amount that has been through binary
        /// floating point no longer compares equal to the payable it should reconcile against.
        /// </summary>
        private static decimal ParseAmount(string raw)
        {
            var text = raw.Trim();

            // OFX says the decimal separator is '.', but Brazilian banks ship '1.234,56' anyway.
            // Whichever of the two appears last is the decimal separator; the other is grouping.
            var lastComma = text.LastIndexOf(',');
            var lastDot = text.LastIndexOf('.');

            text = lastComma > lastDot
                ? text.Replace(".", string.Empty).Replace(',', '.')
                : text.Replace(",", string.Empty);

            // Invariant explicitly, never the ambient culture: the server's locale is not the
            // file's, and on a pt-BR host the ambient parse would read "-1234.56" as -123456.
            if (!decimal.TryParse(
                    text,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                throw new FormatException("The statement carries an amount that is not a number.");
            }

            // decimal(18,2) in the column. Rounding here rather than letting the provider truncate
            // keeps what was matched identical to what was stored.
            return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// OFX dates are <c>YYYYMMDD[HHMMSS[.SSS]][[±h:TZ]]</c>. Only the date survives.
        /// </summary>
        /// <remarks>
        /// Real files carry the timezone suffix — <c>20260810120000[-3:BRT]</c> — and honouring it
        /// would mean converting into whatever zone the server happens to run in, which can shift a
        /// transaction across midnight and move it a day inside the ±3-day window. The bank posts
        /// on a banking day, not an instant, so the date is the whole signal and the clock is noise.
        /// </remarks>
        private static DateTime ParseDate(string raw)
        {
            var text = raw.Trim();

            if (text.Length < 8 || !DateTime.TryParseExact(
                    text[..8],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                throw new FormatException("The statement carries a date that cannot be read.");
            }

            return date;
        }
    }
}
