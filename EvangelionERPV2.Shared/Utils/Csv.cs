using System.Globalization;
using System.Text;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The whole CSV writer: quoting, and nothing else.
    /// </summary>
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

            if (value.IndexOf(Separator) < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
                return value;

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
