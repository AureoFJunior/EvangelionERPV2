namespace EvangelionERPV2.OpportunityRadarModule.Application.Configs
{
    public class OpportunityRadarSettings
    {
        public bool Enabled { get; set; } = false;
        public int DefaultHistoryWindowDays { get; set; } = 180;
        public int MinimumSampleOrders { get; set; } = 3;
    }
}
