using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.EmployeeModule.Test
{
    /// <summary>
    /// The CLT art. 130 step table and the art. 134 §1 split rules, tested at every cliff edge.
    /// </summary>
    public class VacationEntitlementTests
    {
        private static readonly DateTime PeriodStart = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime PeriodEnd = new(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(0, 30)]
        [InlineData(5, 30)]
        [InlineData(6, 24)]
        [InlineData(14, 24)]
        [InlineData(15, 18)]
        [InlineData(23, 18)]
        [InlineData(24, 12)]
        [InlineData(32, 12)]
        [InlineData(33, 0)]
        [InlineData(400, 0)]
        public void EntitledDaysFor_FollowsTheArt130Table(int faltas, int expectedDays)
        {
            Assert.Equal(expectedDays, VacationEntitlement.EntitledDaysFor(faltas));
        }

        [Fact]
        public void CountUnjustifiedFaltas_CountsOnlyTheDaysInsideThePeriod()
        {
            var straddling = Absence(EnumEmployeeAbsenceType.Falta, new DateTime(2025, 12, 30), new DateTime(2026, 1, 3), justified: false);

            Assert.Equal(2, VacationEntitlement.CountUnjustifiedFaltas([straddling], PeriodStart, PeriodEnd));
        }

        [Fact]
        public void CountUnjustifiedFaltas_CountsAnOverlappingDayOnce()
        {
            var first = Absence(EnumEmployeeAbsenceType.Falta, new DateTime(2025, 3, 3), new DateTime(2025, 3, 5), justified: false);
            var second = Absence(EnumEmployeeAbsenceType.Atestado, new DateTime(2025, 3, 4), new DateTime(2025, 3, 6), justified: false);

            Assert.Equal(4, VacationEntitlement.CountUnjustifiedFaltas([first, second], PeriodStart, PeriodEnd));
        }

        [Fact]
        public void CountUnjustifiedFaltas_IgnoresJustifiedAbsences()
        {
            var atestado = Absence(EnumEmployeeAbsenceType.Atestado, new DateTime(2025, 5, 1), new DateTime(2025, 5, 10), justified: true);

            Assert.Equal(0, VacationEntitlement.CountUnjustifiedFaltas([atestado], PeriodStart, PeriodEnd));
        }

        [Fact]
        public void CountUnjustifiedFaltas_IgnoresAdvertencias()
        {
            var advertencia = Absence(EnumEmployeeAbsenceType.Advertencia, new DateTime(2025, 6, 1), new DateTime(2025, 6, 20), justified: false);

            Assert.Equal(0, VacationEntitlement.CountUnjustifiedFaltas([advertencia], PeriodStart, PeriodEnd));
        }

        [Fact]
        public void CountUnjustifiedFaltas_IgnoresVacationEvenWhenSomethingMarkedItUnjustified()
        {
            var vacation = Absence(EnumEmployeeAbsenceType.Vacation, new DateTime(2025, 7, 1), new DateTime(2025, 7, 30), justified: false);

            Assert.Equal(0, VacationEntitlement.CountUnjustifiedFaltas([vacation], PeriodStart, PeriodEnd));
        }

        [Fact]
        public void CountUnjustifiedFaltas_IgnoresSoftDeletedRows()
        {
            var deleted = Absence(EnumEmployeeAbsenceType.Falta, new DateTime(2025, 2, 1), new DateTime(2025, 2, 10), justified: false);
            deleted.IsActive = false;

            Assert.Equal(0, VacationEntitlement.CountUnjustifiedFaltas([deleted], PeriodStart, PeriodEnd));
        }

        [Fact]
        public void TrySplit_AcceptsALegalThreeWaySplit()
        {
            Assert.True(VacationEntitlement.TrySplit(14, [], 16, out _));
            Assert.True(VacationEntitlement.TrySplit(10, [14], 6, out _));
            Assert.True(VacationEntitlement.TrySplit(6, [14, 10], 0, out _));
        }

        [Fact]
        public void TrySplit_RejectsAPeriodShorterThanFiveDays()
        {
            Assert.False(VacationEntitlement.TrySplit(4, [], 26, out var reason));
            Assert.Contains("5 days", reason);
        }

        [Fact]
        public void TrySplit_RejectsAFourthPeriod()
        {
            Assert.False(VacationEntitlement.TrySplit(5, [14, 6, 5], 5, out var reason));
            Assert.Contains("3 periods", reason);
        }

        [Fact]
        public void TrySplit_RejectsASplitThatCanNeverContainALongPeriod()
        {
            Assert.False(VacationEntitlement.TrySplit(8, [12, 10], 0, out var reason));
            Assert.Contains("14 days", reason);
        }

        [Fact]
        public void TrySplit_AllowsAShortFirstPeriodWhileALongOneIsStillReachable()
        {
            Assert.True(VacationEntitlement.TrySplit(6, [], 24, out _));
        }

        [Theory]
        [InlineData(30, 0, 10)]
        [InlineData(30, 10, 0)]
        [InlineData(24, 0, 8)]
        [InlineData(18, 0, 6)]
        [InlineData(12, 0, 4)]
        [InlineData(0, 0, 0)]
        public void MaxSellableDays_CapsTheAbonoAtAThirdOfTheEntitlement(int entitled, int sold, int expected)
        {
            Assert.Equal(expected, VacationEntitlement.MaxSellableDays(entitled, sold));
        }

        private static EmployeeAbsence Absence(EnumEmployeeAbsenceType type, DateTime start, DateTime end, bool justified) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Type = (int)type,
            StartDate = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(end, DateTimeKind.Utc),
            IsJustified = justified,
            IsActive = true
        };
    }
}
