using System.Reflection;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Web.FluentValidator;
using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The period lock itself: the rule, the way a closing date is accepted, and the status the
    /// caller sees when a write lands in a closed month.
    /// </summary>
    public class PeriodLockTests
    {
        private static readonly DateTime ClosedThrough = new(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Assert_WithNoLock_AllowsAnyDate()
        {
            PeriodLock.Assert(null, new DateTime(1999, 1, 1), "This order");
        }

        [Fact]
        public void Assert_OnTheClosingDateItself_Blocks()
        {
            // "Closed through the 31st" includes the 31st, or the last day of every month leaks.
            Assert.Throws<PeriodClosedException>(
                () => PeriodLock.Assert(ClosedThrough, new DateTime(2026, 3, 31), "This order"));
        }

        [Fact]
        public void Assert_OnTheDayAfter_Allows()
        {
            PeriodLock.Assert(ClosedThrough, new DateTime(2026, 4, 1), "This order");
        }

        [Fact]
        public void Assert_BeforeTheClosingDate_Blocks()
        {
            Assert.Throws<PeriodClosedException>(
                () => PeriodLock.Assert(ClosedThrough, new DateTime(2026, 1, 15), "This order"));
        }

        [Fact]
        public void Assert_IgnoresTheTimeOfDayOnBothSides()
        {
            // A lock stamped mid-afternoon must not let the evening of its own day through.
            var lockedAtNoon = new DateTime(2026, 3, 31, 12, 0, 0, DateTimeKind.Utc);

            Assert.Throws<PeriodClosedException>(
                () => PeriodLock.Assert(lockedAtNoon, new DateTime(2026, 3, 31, 23, 59, 0, DateTimeKind.Utc), "This order"));

            PeriodLock.Assert(lockedAtNoon, new DateTime(2026, 4, 1, 0, 1, 0, DateTimeKind.Utc), "This order");
        }

        [Fact]
        public void Assert_SaysWhatWasBlockedAndUntilWhen()
        {
            var exception = Assert.Throws<PeriodClosedException>(
                () => PeriodLock.Assert(ClosedThrough, new DateTime(2026, 2, 10), "This payable bill"));

            Assert.Contains("This payable bill", exception.Message);
            Assert.Contains("2026-02-10", exception.Message);
            Assert.Contains("2026-03-31", exception.Message);
        }

        [Fact]
        public void ClosePeriodValidator_RejectsAFutureClosingDate()
        {
            var result = new ClosePeriodRequestValidator().Validate(
                new ClosePeriodRequestDTO { ClosedThroughUtc = DateTime.UtcNow.Date.AddDays(1) });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void ClosePeriodValidator_AcceptsTodayAndAReopen()
        {
            Assert.True(new ClosePeriodRequestValidator()
                .Validate(new ClosePeriodRequestDTO { ClosedThroughUtc = DateTime.UtcNow.Date }).IsValid);

            // Null is how the books are reopened.
            Assert.True(new ClosePeriodRequestValidator()
                .Validate(new ClosePeriodRequestDTO { ClosedThroughUtc = null }).IsValid);
        }

        [Fact]
        public void Middleware_MapsAClosedPeriodTo409_AndExplainsWhich()
        {
            var environment = new Mock<IHostEnvironment>(MockBehavior.Strict);
            environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Production);
            var middleware = new ExceptionHandlingMiddleware(_ => Task.CompletedTask, environment.Object);

            var statusMethod = typeof(ExceptionHandlingMiddleware)
                .GetMethod("GetStatusCodeFromException", BindingFlags.NonPublic | BindingFlags.Static)!;
            var detailMethod = typeof(ExceptionHandlingMiddleware)
                .GetMethod("GetErrorDetail", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var exception = new PeriodClosedException("This order is dated 2026-02-10, inside the period closed through 2026-03-31.");

            Assert.Equal(StatusCodes.Status409Conflict, (int)statusMethod.Invoke(null, [exception])!);

            // The dates are ours, never submitted values — the caller needs them to understand the 409.
            Assert.Equal(exception.Message, detailMethod.Invoke(middleware, [exception, StatusCodes.Status409Conflict]) as string);
        }
    }
}
