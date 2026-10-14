using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(EnterpriseId), nameof(ExecutedAt))]
    public class ForecastSimulationLog : BaseEntity
    {
        public Guid EnterpriseId { get; set; }
        public Guid UserId { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
        public string ScenarioName { get; set; } = string.Empty;
        public int HorizonInDays { get; set; }
        public double FinalProjectedBalance { get; set; }
        public string InputsJson { get; set; } = string.Empty;
    }
}
