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
