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

        public string? ApiKey { get; set; }
    }
}
