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
    }
}
