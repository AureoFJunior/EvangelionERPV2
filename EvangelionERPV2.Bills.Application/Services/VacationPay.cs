namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// What one stretch of férias costs: the salary for the days off, the 1/3 constitucional on top,
    /// and the abono pecuniário when days were sold.
    /// </summary>
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
            var daysInMonth = DateTime.DaysInMonth(start.Year, start.Month);

            var salaryPortion = RoundMoney(monthlySalary * days / daysInMonth);
            var constitutionalThird = RoundMoney(salaryPortion / ConstitutionalThirdDivisor);

            var abono = soldDays > 0 ? RoundMoney(monthlySalary * soldDays / daysInMonth) : 0m;

            var abonoThird = abono > 0 ? RoundMoney(abono / ConstitutionalThirdDivisor) : 0m;

            return new VacationPayAmounts(salaryPortion, constitutionalThird, abono, abonoThird);
        }

        /// <summary>The day the payment falls due for a stretch starting on <paramref name="start"/>.</summary>
        public static DateTime DueDateFor(DateTime start) => start.Date.AddDays(-PaymentLeadDays);

        private static decimal RoundMoney(decimal value)
        {
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
