namespace EvangelionERPV2.AiModule.Application.Interface
{
    /// <summary>
    /// What was said, and nothing about how it sounded. There is deliberately no audio anywhere in
    /// this type: the clip is transcribed in flight and dropped, and only this text is ever stored.
    /// </summary>
    public sealed record AiTranscript(string Text, string Model, long LatencyMs)
    {
        public static readonly AiTranscript Empty = new(string.Empty, string.Empty, 0);

        public bool HasSpeech => !string.IsNullOrWhiteSpace(Text);
    }

    public sealed record AiTranscriptionResult(AiTranscript Transcript, string? Error)
    {
        public static AiTranscriptionResult Ok(AiTranscript transcript) => new(transcript, null);

        public static AiTranscriptionResult Failed(string error) => new(AiTranscript.Empty, error);

        public bool Success => Error is null;
    }

    public interface IAiSpeechService
    {
        /// <summary>
        /// Turns one spoken utterance into text. The stream is read once and forwarded; it is never
        /// copied to disk, and the caller owns disposing it.
        /// </summary>
        Task<AiTranscriptionResult> TranscribeAsync(
            Stream audio,
            string contentType,
            string fileName,
            CancellationToken cancellation = default);
    }
}
