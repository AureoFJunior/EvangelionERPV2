using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    public static class AiJson
    {
        public const int DefaultCap = 20;

        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// A page of a bigger list. Says which page it is and whether asking for the next one is
        /// worth it, so the model can walk a catalogue instead of stopping at the first twenty.
        /// </summary>
        public static string ForPage<T>(IReadOnlyList<T> rows, int total, int page, int pageSize)
        {
            var seen = (page - 1) * pageSize + rows.Count;

            return JsonSerializer.Serialize(new
            {
                total,
                mostrando = rows.Count,
                pagina = page,
                truncado = seen < total,
                proximaPagina = seen < total ? page + 1 : (int?)null,
                itens = rows,
            }, Options);
        }

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
