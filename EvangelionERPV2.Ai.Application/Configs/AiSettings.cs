namespace EvangelionERPV2.AiModule.Application.Configs
{
    public class AiSettings
    {
        public bool Enabled { get; set; } = false;

        public string Endpoint { get; set; } = "http://localhost:11434";

        public string Model { get; set; } = "qwen3:14b";

        public int MaxToolIterations { get; set; } = 6;

        public int TimeoutSeconds { get; set; } = 120;

        public int MessageRetentionDays { get; set; } = 90;

        /// <summary>How long a proposed write stays confirmable. See <c>AiPendingAction</c>.</summary>
        public int PendingActionTtlMinutes { get; set; } = 30;

        /// <summary>How often the expiry sweep runs. Never below a minute.</summary>
        public int PendingActionSweepIntervalMinutes { get; set; } = 10;

        public string? ApiKey { get; set; }

        #region Voz

        /// <summary>
        /// Speech-to-text off by default, the same posture as <see cref="Enabled"/>. Voice needs a
        /// transcription server running; without one the endpoint answers 503 rather than hanging.
        /// </summary>
        public bool SpeechEnabled { get; set; } = false;

        /// <summary>A local Whisper server. Same shape as <see cref="Endpoint"/>: nothing leaves the machine.</summary>
        public string SpeechEndpoint { get; set; } = "http://localhost:9000";

        public string SpeechModel { get; set; } = "small";

        /// <summary>ISO code handed to Whisper. Guessing costs accuracy on short clips.</summary>
        public string SpeechLanguage { get; set; } = "pt";

        public int SpeechTimeoutSeconds { get; set; } = 30;

        /// <summary>Hard cap on one utterance. Also the client's cut-off while recording.</summary>
        public int MaxAudioSeconds { get; set; } = 60;

        /// <summary>
        /// Hard cap on the upload. Opus at conversation bitrates is ~2 KB/s, so 5 MB is far more
        /// than <see cref="MaxAudioSeconds"/> can produce — it is a backstop, not a budget.
        /// </summary>
        public int MaxAudioBytes { get; set; } = 5 * 1024 * 1024;

        /// <summary>How long silence must last before the client calls the sentence finished.</summary>
        public int SilenceMsToEndTurn { get; set; } = 1000;

        #endregion
    }
}
