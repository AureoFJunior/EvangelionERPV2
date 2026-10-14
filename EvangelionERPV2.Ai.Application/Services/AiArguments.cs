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

        /// <summary>
        /// The id under <paramref name="key"/>, or null when it is missing or not a full GUID.
        /// Local models truncate ids — "5ecc3fd5" instead of the whole thing — often enough that
        /// this check is the difference between a correction and a failed confirmation.
        /// </summary>
        public static Guid? Id(IReadOnlyDictionary<string, object?> arguments, string key) =>
            Guid.TryParse(Text(arguments, key), out var id) && id != Guid.Empty ? id : null;

        public static decimal? Number(IReadOnlyDictionary<string, object?> arguments, string key)
        {
            var text = Text(arguments, key);

            return decimal.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        /// <summary>A copy with one key replaced — how a resolved id gets back into the arguments.</summary>
        public static IReadOnlyDictionary<string, object?> With(
            IReadOnlyDictionary<string, object?> arguments, string key, object? value)
        {
            var copy = new Dictionary<string, object?>(arguments, StringComparer.Ordinal)
            {
                [key] = value,
            };

            return copy;
        }

        /// <summary>The value prefixed by <paramref name="label"/>, or nothing when it is absent.</summary>
        public static string Suffix(IReadOnlyDictionary<string, object?> arguments, string key, string label)
        {
            var text = Text(arguments, key);
            return text.Length == 0 ? string.Empty : label + text;
        }
    }
}
