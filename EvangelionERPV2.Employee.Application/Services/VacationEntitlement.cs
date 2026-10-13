using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.EmployeeModule.Application.Services
{
    /// <summary>
    /// The CLT art. 130 entitlement table and the art. 134 §1 splitting rules, as pure functions.
    /// </summary>
    public static class VacationEntitlement
    {
        /// <summary>A full período aquisitivo is twelve months.</summary>
        public const int AcquisitionMonths = 12;

        /// <summary>The employer has the following twelve months to grant the leave (art. 134).</summary>
        public const int ConcessionMonths = 12;

        /// <summary>art. 134 §1: the leave may be broken into at most three stretches.</summary>
        public const int MaxPeriods = 3;

        /// <summary>art. 134 §1: one of those stretches must be at least fourteen days.</summary>
        public const int MinLongPeriodDays = 14;

        /// <summary>art. 134 §1: none of them may be shorter than five days.</summary>
        public const int MinPeriodDays = 5;

        /// <summary>art. 143: at most a third of the entitlement may be sold.</summary>
        public const int AbonoDivisor = 3;

        /// <summary>art. 143: the abono must be requested at least fifteen days before the aquisitivo closes.</summary>
        public const int AbonoRequestDeadlineDays = 15;

        /// <summary>
        /// Days earned by one período aquisitivo, from the art. 130 step table applied to the
        /// unjustified faltas inside it.
        /// </summary>
        public static int EntitledDaysFor(int unjustifiedFaltas) => unjustifiedFaltas switch
        {
            <= 5 => 30,
            <= 14 => 24,
            <= 23 => 18,
            <= 32 => 12,
            _ => 0
        };

        /// <summary>
        /// Counts the calendar days inside <paramref name="periodStart"/>..<paramref name="periodEnd"/>
        /// that an unjustified absence took away.
        /// </summary>
        public static int CountUnjustifiedFaltas(
            IEnumerable<EmployeeAbsence> absences, DateTime periodStart, DateTime periodEnd)
        {
            ArgumentNullException.ThrowIfNull(absences);

            var start = periodStart.Date;
            var end = periodEnd.Date;
            var days = new HashSet<DateTime>();

            foreach (var absence in absences.Where(CountsAsFalta))
            {
                var from = absence.StartDate.Date > start ? absence.StartDate.Date : start;
                var to = absence.EndDate.Date < end ? absence.EndDate.Date : end;

                for (var day = from; day <= to; day = day.AddDays(1))
                    days.Add(day);
            }

            return days.Count;
        }

        /// <summary>
        /// What art. 130 means by "falta injustificada": days off that the employee is answerable
        /// for.
        /// </summary>
        private static bool CountsAsFalta(EmployeeAbsence absence)
        {
            return absence.IsActive == true
                && !absence.IsJustified
                && absence.Type != (int)EnumEmployeeAbsenceType.Advertencia
                && absence.Type != (int)EnumEmployeeAbsenceType.Vacation;
        }

        /// <summary>
        /// Whether a proposed stretch of leave is legal given the ones already booked against the
        /// same accrual.
        /// </summary>
        /// <param name="requestedDays">Length of the stretch being scheduled.</param>
        /// <param name="existingPeriodLengths">Lengths of the stretches already booked.</param>
        /// <param name="remainingDaysAfter">Days left on the accrual once this one is taken.</param>
        /// <param name="reason">Why it was refused, when it was.</param>
        public static bool TrySplit(
            int requestedDays,
            IReadOnlyCollection<int> existingPeriodLengths,
            int remainingDaysAfter,
            out string reason)
        {
            ArgumentNullException.ThrowIfNull(existingPeriodLengths);

            if (requestedDays < MinPeriodDays)
            {
                reason = $"A férias period cannot be shorter than {MinPeriodDays} days (CLT art. 134 §1).";
                return false;
            }

            if (existingPeriodLengths.Count >= MaxPeriods)
            {
                reason = $"Férias may be split into at most {MaxPeriods} periods (CLT art. 134 §1).";
                return false;
            }

            var hasLongPeriod = existingPeriodLengths.Any(length => length >= MinLongPeriodDays)
                || requestedDays >= MinLongPeriodDays;

            var canStillBookALongPeriod = remainingDaysAfter >= MinLongPeriodDays
                && existingPeriodLengths.Count + 1 < MaxPeriods;

            if (!hasLongPeriod && !canStillBookALongPeriod)
            {
                reason = $"One férias period must be at least {MinLongPeriodDays} days, and this schedule leaves no room for one (CLT art. 134 §1).";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>Days that may still be sold as abono, given what the accrual is worth and what is gone.</summary>
        public static int MaxSellableDays(int entitledDays, int alreadySoldDays) =>
            Math.Max(0, entitledDays / AbonoDivisor - alreadySoldDays);
    }
}
