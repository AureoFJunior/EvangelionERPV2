namespace EvangelionERPV2.Shared.DTOs
{
    public class CashFlowForecastDayDTO
    {
        public DateTime Date { get; set; }
        public double AccountsReceivable { get; set; }
        public double AccountsPayable { get; set; }
        public double ProjectedBalance { get; set; }
        public bool IsRiskDay { get; set; }
    }

    public class CashFlowForecastDTO
    {
        public int HorizonInDays { get; set; }
        public double CurrentBalance { get; set; }
        public double FinalProjectedBalance { get; set; }
        public IEnumerable<CashFlowForecastDayDTO> DailyProjection { get; set; } = [];
    }

    public class ForecastSimulationScenarioDTO
    {
        public string ScenarioName { get; set; } = string.Empty;
        public int ReceivableDelayInDays { get; set; }
        public double PayableMultiplier { get; set; } = 1;
    }

    public class RunSimulationRequestDTO
    {
        public int HorizonInDays { get; set; }
        public double? CurrentBalance { get; set; }
        public IEnumerable<ForecastSimulationScenarioDTO> Scenarios { get; set; } = [];
    }

    public class SimulationResultDTO
    {
        public string ScenarioName { get; set; } = string.Empty;
        public double FinalProjectedBalance { get; set; }
        public double Impact { get; set; }
        public IEnumerable<DateTime> RiskDays { get; set; } = [];
    }
}
