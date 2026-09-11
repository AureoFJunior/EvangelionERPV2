using EvangelionERPV2.NotificationModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.NotificationModule.Test.Notification
{
    /// <summary>
    /// The alert map is a pure function, and every boundary in it is a bug someone would otherwise
    /// ship: an alert announcing "0 overdue", a certificate that was never configured reported as
    /// expiring, an expiry that fires a day late.
    /// </summary>
    public class NotificationAlertsTests
    {
        private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void AQuietDay_FiresNothing()
        {
            var alerts = NotificationAlerts.From(new DailyPanelDTO(), Now);

            // Never an item that says zero of anything.
            Assert.Empty(alerts);
        }

        [Fact]
        public void OverdueReceivables_FireAWarningWithTheAmount()
        {
            var panel = new DailyPanelDTO();
            panel.Receivables.OverdueCount = 4;
            panel.Receivables.OverdueAmount = 9226.24m;

            var alert = Assert.Single(NotificationAlerts.From(panel, Now));

            Assert.Equal("receivables.overdue", alert.Key);
            Assert.Equal(EnumNotificationSeverity.Warning, alert.Severity);
            Assert.Contains("4 parcelas vencidas", alert.Body);
            Assert.Contains("9.226,24", alert.Body);
        }

        [Fact]
        public void OverduePayables_AreCritical_NotMerelyWarned()
        {
            var panel = new DailyPanelDTO();
            panel.Payables.OverdueCount = 1;

            var alert = Assert.Single(NotificationAlerts.From(panel, Now));

            Assert.Equal("payables.overdue", alert.Key);
            Assert.Equal(EnumNotificationSeverity.Critical, alert.Severity);
            // One of something reads as one, not "1 contas".
            Assert.Contains("1 conta vencida", alert.Body);
        }

        [Fact]
        public void NoCertificateConfigured_IsNotAnExpiringCertificate()
        {
            var panel = new DailyPanelDTO();
            panel.Fiscal.CertificateDaysRemaining = null;
            panel.Fiscal.CertificateConfigured = false;

            // Null means nobody uploaded one. That is a different fact from one about to expire,
            // and the panel already says so through CertificateConfigured.
            Assert.Empty(NotificationAlerts.From(panel, Now));
        }

        [Theory]
        [InlineData(30, true)]
        [InlineData(31, false)]
        [InlineData(0, true)]
        public void TheCertificateWindowIsExactlyThirtyDays(int daysRemaining, bool shouldFire)
        {
            var panel = new DailyPanelDTO();
            panel.Fiscal.CertificateDaysRemaining = daysRemaining;

            var alerts = NotificationAlerts.From(panel, Now);

            Assert.Equal(shouldFire, alerts.Any(alert => alert.Key == "fiscal.certExpiring"));
        }

        [Fact]
        public void AnExpiredCertificate_SaysExpired_NotExpiresInMinusFourDays()
        {
            var panel = new DailyPanelDTO();
            panel.Fiscal.CertificateDaysRemaining = -4;

            var alert = Assert.Single(NotificationAlerts.From(panel, Now));

            Assert.Equal("Certificado vencido", alert.Title);
            Assert.Contains("venceu há 4 dias", alert.Body);
            Assert.DoesNotContain("-4", alert.Body);
        }

        [Fact]
        public void ACertificateExpiringToday_SaysToday()
        {
            var panel = new DailyPanelDTO();
            panel.Fiscal.CertificateDaysRemaining = 0;

            var alert = Assert.Single(NotificationAlerts.From(panel, Now));

            Assert.Contains("vence hoje", alert.Body);
        }

        [Fact]
        public void EveryAlert_CarriesAPermissionAndAUniqueKey()
        {
            var alerts = NotificationAlerts.From(EverythingOnFire(), Now);

            Assert.Equal(10, alerts.Count);
            Assert.All(alerts, alert => Assert.False(string.IsNullOrWhiteSpace(alert.RequiredPermission)));
            Assert.All(alerts, alert => Assert.Equal(NotificationSources.Alert, alert.Source));
            // An alert has no row, so it can never be "read".
            Assert.All(alerts, alert => Assert.Null(alert.Id));
            Assert.All(alerts, alert => Assert.False(alert.IsRead));
            Assert.Equal(alerts.Count, alerts.Select(alert => alert.Key).Distinct().Count());
        }

        [Fact]
        public void ThePayablesAlert_IsGuardedByTheDashboardPermission()
        {
            var panel = new DailyPanelDTO();
            panel.Payables.OverdueCount = 1;
            panel.Stock.CriticalCount = 1;

            var alerts = NotificationAlerts.From(panel, Now);

            // The tenant group contains Employees; these two are not equally readable.
            Assert.Equal("dashboard.read", alerts.Single(a => a.Key == "payables.overdue").RequiredPermission);
            Assert.Equal("products.read", alerts.Single(a => a.Key == "stock.critical").RequiredPermission);
        }

        [Fact]
        public void TheFingerprintTracksWhichAlertsFire_NotTheirCounts()
        {
            var three = new DailyPanelDTO();
            three.Payables.OverdueCount = 3;

            var four = new DailyPanelDTO();
            four.Payables.OverdueCount = 4;

            // 3 overdue becoming 4 is the same alert. Re-pinging every open browser for it would
            // teach people to ignore the bell.
            Assert.Equal(
                NotificationAlerts.Fingerprint(NotificationAlerts.From(three, Now)),
                NotificationAlerts.Fingerprint(NotificationAlerts.From(four, Now)));
        }

        [Fact]
        public void TheFingerprintChanges_WhenANewAlertStartsFiring()
        {
            var one = new DailyPanelDTO();
            one.Payables.OverdueCount = 3;

            var two = new DailyPanelDTO();
            two.Payables.OverdueCount = 3;
            two.Bank.UnclearedCount = 1;

            Assert.NotEqual(
                NotificationAlerts.Fingerprint(NotificationAlerts.From(one, Now)),
                NotificationAlerts.Fingerprint(NotificationAlerts.From(two, Now)));
        }

        private static DailyPanelDTO EverythingOnFire()
        {
            var panel = new DailyPanelDTO();
            panel.Receivables.OverdueCount = 2;
            panel.Receivables.DueTodayCount = 1;
            panel.Payables.OverdueCount = 1;
            panel.Payables.DueTodayCount = 1;
            panel.Stock.CriticalCount = 3;
            panel.Fiscal.RejectedCount = 1;
            panel.Fiscal.PendingCount = 2;
            panel.Fiscal.OutcomeUnknownCount = 1;
            panel.Fiscal.CertificateDaysRemaining = 5;
            panel.Bank.UnclearedCount = 7;

            return panel;
        }
    }
}
