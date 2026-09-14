using System.Globalization;
using System.Text.RegularExpressions;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Rewrites money into the words a person would say, just before it is spoken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spoken-answer prompt already asks the model to write "nove mil duzentos e vinte e seis
    /// reais" rather than "R$ 9.226,24", and the model mostly complies — but "mostly" is the problem.
    /// A figure is the whole point of most answers here, and a 14B model that slips back into digits
    /// once in ten turns produces an answer nobody can use, with no way to tell from the text that
    /// anything went wrong. This pass is deterministic, so the figure is right every time regardless
    /// of what the model wrote.
    /// </para>
    /// <para>
    /// Deliberately only currency. Bare numbers are dates, quantities, order numbers and product
    /// codes, and Piper reads most of those acceptably — rewriting them all would turn "pedido 1234"
    /// into "pedido mil duzentos e trinta e quatro" and 14/09 into something worse.
    /// </para>
    /// </remarks>
    public static class AiSpokenText
    {
        /// <summary>R$ followed by a pt-BR formatted amount: thousands separated by dots, two decimals after a comma.</summary>
        private static readonly Regex CurrencyPattern = new(
            @"R\$\s*(\d{1,3}(?:\.\d{3})*|\d+)(?:,(\d{1,2}))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] Units =
        [
            "zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
            "onze", "doze", "treze", "catorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"
        ];

        private static readonly string[] Tens =
        [
            "", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"
        ];

        private static readonly string[] Hundreds =
        [
            "", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos",
            "seiscentos", "setecentos", "oitocentos", "novecentos"
        ];

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return CurrencyPattern.Replace(text, match =>
            {
                var reais = long.Parse(match.Groups[1].Value.Replace(".", string.Empty), CultureInfo.InvariantCulture);

                // "R$ 5,5" is five reais and fifty centavos, not five and five.
                var centavos = match.Groups[2].Success
                    ? int.Parse(match.Groups[2].Value.PadRight(2, '0'), CultureInfo.InvariantCulture)
                    : 0;

                return Money(reais, centavos);
            });
        }

        private static string Money(long reais, int centavos)
        {
            var parts = new List<string>();

            // Zero reais is only worth saying when there is nothing else to say.
            if (reais > 0 || centavos == 0)
                parts.Add($"{Spell(reais)} {Reais(reais)}");

            if (centavos > 0)
                parts.Add($"{Spell(centavos)} {(centavos == 1 ? "centavo" : "centavos")}");

            return string.Join(" e ", parts);
        }

        /// <summary>
        /// A round million or billion takes "de": "um milhão de reais", not "um milhão reais". With
        /// anything below the scale still to say, it does not — "dois milhões e quinhentos mil reais".
        /// </summary>
        private static string Reais(long reais)
        {
            if (reais == 1)
                return "real";

            return reais >= 1_000_000 && reais % 1_000_000 == 0 ? "de reais" : "reais";
        }

        /// <summary>Whole numbers up to just under a trillion, which is past anything this ERP holds.</summary>
        private static string Spell(long number)
        {
            if (number < 0)
                return "menos " + Spell(-number);

            if (number < 1000)
                return SpellBelowThousand((int)number);

            foreach (var (scale, singular, plural) in Scales)
            {
                var count = number / scale;
                if (count == 0)
                    continue;

                var rest = number % scale;

                // "mil", never "um mil". Every other scale keeps its one.
                var head = scale == 1000 && count == 1
                    ? "mil"
                    : $"{Spell(count)} {(count == 1 ? singular : plural)}";

                return rest == 0 ? head : $"{head}{Joiner(rest)}{Spell(rest)}";
            }

            return SpellBelowThousand((int)number);
        }

        /// <summary>
        /// Portuguese joins the tail with "e" when it is under a hundred or a round number —
        /// "dois mil e cinco", "mil e cem", "dois milhões e quinhentos mil" — and simply runs on
        /// otherwise: "mil duzentos e trinta e quatro".
        /// </summary>
        private static string Joiner(long rest) => rest < 100 || rest % 100 == 0 ? " e " : " ";

        private static readonly (long Scale, string Singular, string Plural)[] Scales =
        [
            (1_000_000_000, "bilhão", "bilhões"),
            (1_000_000, "milhão", "milhões"),
            (1000, "mil", "mil"),
        ];

        private static string SpellBelowThousand(int number)
        {
            if (number < 20)
                return Units[number];

            if (number < 100)
            {
                var tens = Tens[number / 10];
                var unit = number % 10;

                return unit == 0 ? tens : $"{tens} e {Units[unit]}";
            }

            // "cem" alone, "cento e ..." with anything after it.
            if (number == 100)
                return "cem";

            var hundreds = Hundreds[number / 100];
            var rest = number % 100;

            return rest == 0 ? hundreds : $"{hundreds} e {SpellBelowThousand(rest)}";
        }
    }
}
