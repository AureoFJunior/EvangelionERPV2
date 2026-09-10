using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Reads model-supplied arguments for the confirmation card. They arrive as whatever the JSON
    /// parser made of them — <see cref="JsonElement"/>, a boxed number, a string, or nothing at all —
    /// so every read goes through here rather than casting at the call site.
    /// </summary>
    public static class AiArguments
    {
        public static string Text(IReadOnlyDictionary<string, object?> arguments, string key)
        {
            if (!arguments.TryGetValue(key, out var value) || value is null)
                return string.Empty;

            return value switch
            {
                string text => text.Trim(),
                JsonElement element when element.ValueKind == JsonValueKind.String => (element.GetString() ?? string.Empty).Trim(),
                JsonElement element when element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined => string.Empty,
                JsonElement element => element.ToString(),
                _ => value.ToString()?.Trim() ?? string.Empty,
            };
        }

        /// <summary>The value prefixed by <paramref name="label"/>, or nothing when it is absent.</summary>
        public static string Suffix(IReadOnlyDictionary<string, object?> arguments, string key, string label)
        {
            var text = Text(arguments, key);
            return text.Length == 0 ? string.Empty : label + text;
        }
    }
}
