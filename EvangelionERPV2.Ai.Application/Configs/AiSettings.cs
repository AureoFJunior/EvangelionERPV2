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

        /// <summary>
        /// Sent with every clip, so it overrides whatever the server was started with. Measured
        /// 2026-09-14 on noisy speech: medium halves the errors of small (14.0% against 22.1% word
        /// error) and, quantised to int8, costs 2.3 s against 1.4 s per utterance.
        /// </summary>
        public string SpeechModel { get; set; } = "Systran/faster-whisper-medium";

        /// <summary>ISO code handed to Whisper. Guessing costs accuracy on short clips.</summary>
        public string SpeechLanguage { get; set; } = "pt";

        public int SpeechTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Drops the silence before Whisper ever sees it. Fed a quiet clip, Whisper does not answer
        /// "nothing was said" — it answers with one phrase repeated until it runs out of room, which
        /// is how a 667-character question nobody asked reached the conversation history.
        /// </summary>
        public bool SpeechVadFilter { get; set; } = true;

        /// <summary>
        /// Vocabulary hint, sent with every clip. Whisper weights its guesses toward words it has
        /// just been shown, and this ERP's words are exactly the ones a general model gets wrong:
        /// measured on noisy clips, "estoque crítico hoje" came back as "toque, que de cor".
        /// </summary>
        public string SpeechPrompt { get; set; } =
            "Sistema ERP. Termos: estoque crítico, contas a pagar, contas a receber, parcelas vencidas, "
            + "faturamento, nota fiscal, pedido, cliente, fornecedor, produto, painel do dia, "
            + "fluxo de caixa, inadimplentes, boleto, orçamento, estorno.";

        /// <summary>Hard cap on one utterance. Also the client's cut-off while recording.</summary>
        public int MaxAudioSeconds { get; set; } = 60;

        /// <summary>
        /// Hard cap on the upload. Opus at conversation bitrates is ~2 KB/s, so 5 MB is far more
        /// than <see cref="MaxAudioSeconds"/> can produce — it is a backstop, not a budget.
        /// </summary>
        public int MaxAudioBytes { get; set; } = 5 * 1024 * 1024;

        /// <summary>How long silence must last before the client calls the sentence finished.</summary>
        public int SilenceMsToEndTurn { get; set; } = 1000;

        /// <summary>
        /// Text-to-speech off by default, like every other half of voice. Off, the client keeps the
        /// device's own voice — which is why the endpoint answers 503 instead of failing silently.
        /// </summary>
        public bool VoiceEnabled { get; set; } = false;

        /// <summary>A local Piper server, OpenAI-shaped at <c>/v1/audio/speech</c>.</summary>
        public string VoiceEndpoint { get; set; } = "http://localhost:9100";

        /// <summary>The OpenAI-compatible model name the server expects; Piper ignores the value itself.</summary>
        public string VoiceModel { get; set; } = "tts-1";

        /// <summary>Which voice speaks. One choice for every platform, which is the point of this path.</summary>
        public string VoiceName { get; set; } = "pt_BR-faber-medium";

        /// <summary>mp3 or wav. mp3 is a third of the bytes over the wire for the same sentence.</summary>
        public string VoiceFormat { get; set; } = "mp3";

        public int VoiceTimeoutSeconds { get; set; } = 20;

        /// <summary>
        /// Backstop for a "sentence" that is really a paragraph. The client already splits an answer
        /// into sentences, so a request anywhere near this cap means the splitter let something odd
        /// through, not that a person wrote a long question.
        /// </summary>
        public int MaxSpeechChars { get; set; } = 600;

        #endregion
    }
}
