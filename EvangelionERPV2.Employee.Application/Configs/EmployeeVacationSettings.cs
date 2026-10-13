namespace EvangelionERPV2.EmployeeModule.Application.Configs
{
    /// <summary>
    /// Knobs for the concessivo lapse sweep. Configuration rather than constants: the warning
    /// horizon is how much notice HR wants before a férias period turns into a double-pay
    /// liability, and that is a business preference, not a property of the law.
    /// </summary>
    public class EmployeeVacationSettings
    {
        /// <summary>How often the sweep runs. Clamped to at least one minute.</summary>
        public int LapseSweepIntervalMinutes { get; set; } = 720;

        /// <summary>
        /// How far ahead <c>GetApproachingDeadlineAsync</c> looks. The deadline is a fixed date
        /// known 24 months in advance, so this is about when someone wants to be told, not about
        /// detection.
        /// </summary>
        public int DeadlineWarningHorizonDays { get; set; } = 60;

        /// <summary>Rows per sweep. Bounds one pass; the next pass takes whatever is left.</summary>
        public int LapseSweepBatchSize { get; set; } = 200;
    }
}
