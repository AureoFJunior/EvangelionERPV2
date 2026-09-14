namespace EvangelionERPV2.AiModule.Application.Interface
{
    /// <summary>
    /// One sentence, rendered. The audio is an open stream that has not been read: the caller copies
    /// it to the response and disposes it, and it never lands in a byte array, a file or a cache.
    /// </summary>
    public sealed record AiVoiceResult(Stream? Audio, string ContentType, long LatencyMs, string? Error)
    {
        public static AiVoiceResult Ok(Stream audio, string contentType, long latencyMs) =>
            new(audio, contentType, latencyMs, null);

        public static AiVoiceResult Failed(string error) =>
            new(null, string.Empty, 0, error);

        public bool Success => Error is null && Audio is not null;
    }

    public interface IAiVoiceService
    {
        /// <summary>
        /// Renders one sentence to audio. The returned stream is live — reading it is what pulls the
        /// bytes from the synthesis server — so the caller owns disposing it.
        /// </summary>
        Task<AiVoiceResult> SynthesizeAsync(string text, CancellationToken cancellation = default);
    }
}
