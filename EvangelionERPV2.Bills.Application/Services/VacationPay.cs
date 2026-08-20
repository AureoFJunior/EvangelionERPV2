namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// What one stretch of férias costs: the salary for the days off, the 1/3 constitucional on top,
    /// and the abono pecuniário when days were sold.
    /// </summary>
    /// <remarks>
    /// Pure, and separate from the service for the same reason <c>VacationEntitlement</c> is separate
    /// in the Employee module — these are the numbers an auditor recomputes by hand, so they must be
    /// checkable without a repository in the way.
    /// </remarks>
    public static class VacationPay
    {
        /// <summary>CF art. 7 XVII: férias are paid at a third above the ordinary salary.</summary>
        public const int ConstitutionalThirdDivisor = 3;

        /// <summary>CLT art. 145: payment falls due at the latest two days before the leave starts.</summary>
        public const int PaymentLeadDays = 2;

        /// <summary>
        /// Prices one scheduled stretch.
        /// </summary>
        /// <param name="monthlySalary">The employee's monthly salary.</param>
        /// <param name="days">Length of this stretch, in calendar days.</param>
        /// <param name="soldDays">
        /// Abono pecuniário days to settle with this stretch, or zero. The abono is a single
        /// conversion of days into cash for the whole período, so it rides on one stretch rather
        /// than being spread across them.
        /// </param>
        /// <param name="start">First day of the stretch — its month sets the daily rate.</param>
        public static VacationPayAmounts For(decimal monthlySalary, int days, int soldDays, DateTime start)
        {
            // The same proration PayrollService.BuildDetail uses: salary * days / daysInMonth, in one
            // expression rather than rounding a daily rate first. Two roundings on a 31-day month
            // drift by cents, and this figure has to reconcile against the payroll it replaces.
            var daysInMonth = DateTime.DaysInMonth(start.Year, start.Month);

            var salaryPortion = RoundMoney(monthlySalary * days / daysInMonth);
            var constitutionalThird = RoundMoney(salaryPortion / ConstitutionalThirdDivisor);

            var abono = soldDays > 0 ? RoundMoney(monthlySalary * soldDays / daysInMonth) : 0m;

            // The abono carries its own 1/3: it is remuneration for férias days, and CF art. 7 XVII
            // attaches to the férias remuneration rather than to the act of taking the leave.
            var abonoThird = abono > 0 ? RoundMoney(abono / ConstitutionalThirdDivisor) : 0m;

            return new VacationPayAmounts(salaryPortion, constitutionalThird, abono, abonoThird);
        }

        /// <summary>The day the payment falls due for a stretch starting on <paramref name="start"/>.</summary>
        public static DateTime DueDateFor(DateTime start) => start.Date.AddDays(-PaymentLeadDays);

        private static decimal RoundMoney(decimal value)
        {
            // Half away from zero, matching PayrollService and the decimal(18,2) money columns.
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>The split of one férias payment. <see cref="Total"/> is what the bill is worth.</summary>
    public readonly record struct VacationPayAmounts(
        decimal SalaryPortion,
        decimal ConstitutionalThird,
        decimal AbonoAmount,
        decimal AbonoConstitutionalThird)
    {
        public decimal Total => SalaryPortion + ConstitutionalThird + AbonoAmount + AbonoConstitutionalThird;
    }
}
