using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    public static class AiJson
    {
        public const int DefaultCap = 20;

        /// <summary>
        /// One dialect for every tool result. Two of them in one context window is confusing to the
        /// model, and the relaxed encoder is what stops every "ã" costing six tokens as \u00e3 —
        /// which, in a pt-BR feature, is most of the characters.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            // Compact on purpose: every space is a token, and a tool result is read by a model, not
            // by a person. System.Text.Json throughout, as everywhere else in this solution.
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Wraps rows in an envelope that admits what it left out. Without the count the model
        /// cannot tell "these are all of them" from "these are the first twenty", and it will
        /// confidently assert the former.
        /// </summary>
        public static string ForModel<T>(IReadOnlyList<T> rows, int totalFound, int cap = DefaultCap)
        {
            var shown = rows.Count <= cap ? rows : rows.Take(cap).ToList();

            return JsonSerializer.Serialize(new
            {
                total = totalFound,
                mostrando = shown.Count,
                truncado = totalFound > shown.Count,
                itens = shown,
            }, Options);
        }
    }
}
