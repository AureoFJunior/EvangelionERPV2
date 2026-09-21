namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Catches Whisper's repetition hallucination: fed silence or room noise, it does not answer
    /// "nothing was said" — it answers with one short phrase repeated until it runs out of room.
    /// </summary>
    public static class AiTranscriptSanity
    {
        /// <summary>
        /// Shorter than this, repetition is ordinary speech — "sim, sim, sim", "não não não" — and
        /// nobody would mistake it for a hallucination.
        /// </summary>
        private const int MinimumWordsToJudge = 20;

        /// <summary>
        /// Below this share of distinct words the text is a loop. Real speech of twenty words or more
        /// runs well above half; the observed hallucination ran at three distinct words in a hundred
        /// and seventy.
        /// </summary>
        private const double MaximumDistinctShare = 0.15;

        private static readonly char[] WordBreaks =
            [' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '…'];

        /// <summary>
        /// Drops a sentence that is immediately repeated, keeping the first.
        /// </summary>
        public static string CollapseRepeatedSentences(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;

            var sentences = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sentences.Length < 2)
                return text;

            var kept = new List<string>();
            foreach (var sentence in sentences)
            {
                if (kept.Count == 0 || !string.Equals(kept[^1], sentence, StringComparison.OrdinalIgnoreCase))
                    kept.Add(sentence);
            }

            return kept.Count == sentences.Length ? text : string.Join(". ", kept) + ".";
        }

        public static bool IsRepetitionLoop(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var words = text.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (words.Length < MinimumWordsToJudge)
                return false;

            var distinct = words
                .Select(word => word.ToLowerInvariant())
                .Distinct()
                .Count();

            return (double)distinct / words.Length <= MaximumDistinctShare;
        }
    }
}
