namespace EvangelionERPV2.AiModule.Application.Configs
{
    public class AiSettings
    {
        /// <summary>Off until somebody turns it on, as with the Opportunity Radar.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// From inside the API container this is http://host.docker.internal:11434 — Ollama runs
        /// natively on the host because the GPU is the entire point of running it locally.
        /// </summary>
        public string Endpoint { get; set; } = "http://localhost:11434";

        public string Model { get; set; } = "qwen3:14b";

        /// <summary>
        /// How many tool rounds one question may take before the loop gives up. A local model that
        /// has lost the thread will otherwise call the same tool until the request times out.
        /// </summary>
        public int MaxToolIterations { get; set; } = 6;

        /// <summary>
        /// Budget for one whole turn, tool rounds included — enforced by AiAgentService, not by the
        /// HTTP client, which OllamaApiClient builds itself. Overrunning it ends the turn with an
        /// honest answer rather than an exception.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// AiMessage rows hold customer data, so LGPD requires an answer to how long they live.
        /// The sweep that enforces this is not built in Rung 1; the number is recorded here so the
        /// decision is not lost.
        /// </summary>
        public int MessageRetentionDays { get; set; } = 90;

        /// <summary>
        /// Unused with Ollama, and present so that pointing this module at a hosted provider is a
        /// configuration change rather than also a config-shape migration. When populated it is a
        /// secret reference resolved through AWSKMSKeyProvider, exactly like RecaptchaSettings.
        /// </summary>
        public string? ApiKey { get; set; }
    }
}
