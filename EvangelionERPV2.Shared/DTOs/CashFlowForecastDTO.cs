namespace EvangelionERPV2.Shared.DTOs
{
    public class CashFlowForecastDayDTO
    {
        public DateTime Date { get; set; }
        public decimal AccountsReceivable { get; set; }
        public decimal AccountsPayable { get; set; }
        public decimal ProjectedBalance { get; set; }
        public bool IsRiskDay { get; set; }
    }

    public class CashFlowForecastDTO
    {
        public int HorizonInDays { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal FinalProjectedBalance { get; set; }
        public IEnumerable<CashFlowForecastDayDTO> DailyProjection { get; set; } = [];
    }

    public class ForecastSimulationScenarioDTO
    {
        public string ScenarioName { get; set; } = string.Empty;
        public int ReceivableDelayInDays { get; set; }
        public decimal PayableMultiplier { get; set; } = 1;
    }

    public class RunSimulationRequestDTO
    {
        public int HorizonInDays { get; set; }
        public decimal? CurrentBalance { get; set; }
        public IEnumerable<ForecastSimulationScenarioDTO> Scenarios { get; set; } = [];
    }

    public class SimulationResultDTO
    {
        public string ScenarioName { get; set; } = string.Empty;
        public decimal FinalProjectedBalance { get; set; }
        public decimal Impact { get; set; }
        public IEnumerable<DateTime> RiskDays { get; set; } = [];
    }
}
