using System.Globalization;
using System.Text;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The whole CSV writer: quoting, and nothing else.
    /// </summary>
    /// <remarks>
    /// Semicolon-separated and written with a BOM, because the only reader that matters here is
    /// Excel in pt-BR, which splits on the list separator of the machine's locale and shows a comma
    /// decimal as text without one. That is a deliberate departure from RFC 4180's comma; the
    /// quoting rules below are RFC 4180's.
    ///
    /// No CsvHelper. This is fifteen lines and a dependency is forever.
    /// </remarks>
    public static class Csv
    {
        public const char Separator = ';';

        /// <summary>UTF-8 with a BOM: without it Excel reads accented names as mojibake.</summary>
        public static Encoding Encoding { get; } = new UTF8Encoding(true);

        public static string Line(params string?[] fields)
        {
            return string.Join(Separator, (fields ?? []).Select(Escape)) + "\r\n";
        }

        /// <summary>Invariant with two decimals, so a reader never has to guess the scale.</summary>
        public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>ISO date: unambiguous between pt-BR and en-US readers.</summary>
        public static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

        private static string Escape(string? field)
        {
            var value = field ?? string.Empty;

            // A field only needs quoting if it carries the separator, a quote or a line break —
            // and inside quotes, a quote is doubled.
            if (value.IndexOf(Separator) < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
                return value;

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
