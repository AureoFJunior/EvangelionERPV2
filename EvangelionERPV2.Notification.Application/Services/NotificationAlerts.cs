using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using System.Globalization;

namespace EvangelionERPV2.NotificationModule.Application.Services
{
    /// <summary>
    /// Turns the daily panel into the alerts the bell shows. A pure function over a DTO: no I/O, no
    /// clock of its own, no dependencies — which is the point, because every boundary here is a bug
    /// someone would otherwise ship (an alert saying "0 overdue", a certificate that is not
    /// configured reported as expiring, an expiry that fires a day late).
    /// </summary>
    public static class NotificationAlerts
    {
        /// <summary>A certificate this close to expiry is worth waking someone for.</summary>
        private const int CertificateWarningDays = 30;

        private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

        public static IReadOnlyList<NotificationItemDTO> From(DailyPanelDTO panel, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(panel);

            var alerts = new List<NotificationItemDTO>();

            if (panel.Receivables.OverdueCount > 0)
            {
                alerts.Add(Alert(
                    "receivables.overdue",
                    EnumNotificationSeverity.Warning,
                    "Recebimentos vencidos",
                    $"{Titles(panel.Receivables.OverdueCount, "parcela vencida", "parcelas vencidas")}, " +
                    $"somando {Money(panel.Receivables.OverdueAmount)}.",
                    "dashboard.read",
                    utcNow));
            }

            if (panel.Receivables.DueTodayCount > 0)
            {
                alerts.Add(Alert(
                    "receivables.dueToday",
                    EnumNotificationSeverity.Info,
                    "Recebimentos para hoje",
                    $"{Titles(panel.Receivables.DueTodayCount, "parcela vence", "parcelas vencem")} hoje, " +
                    $"somando {Money(panel.Receivables.DueTodayAmount)}.",
                    "dashboard.read",
                    utcNow));
            }

            if (panel.Payables.OverdueCount > 0)
            {
                alerts.Add(Alert(
                    "payables.overdue",
                    EnumNotificationSeverity.Critical,
                    "Contas a pagar vencidas",
                    $"{Titles(panel.Payables.OverdueCount, "conta vencida", "contas vencidas")}, " +
                    $"somando {Money(panel.Payables.OverdueAmount)}.",
                    "dashboard.read",
                    utcNow));
            }

            if (panel.Payables.DueTodayCount > 0)
            {
                alerts.Add(Alert(
                    "payables.dueToday",
                    EnumNotificationSeverity.Warning,
                    "Contas a pagar hoje",
                    $"{Titles(panel.Payables.DueTodayCount, "conta vence", "contas vencem")} hoje, " +
                    $"somando {Money(panel.Payables.DueTodayAmount)}.",
                    "dashboard.read",
                    utcNow));
            }

            if (panel.Stock.CriticalCount > 0)
            {
                alerts.Add(Alert(
                    "stock.critical",
                    EnumNotificationSeverity.Warning,
                    "Estoque crítico",
                    $"{Titles(panel.Stock.CriticalCount, "produto está", "produtos estão")} em nível crítico.",
                    "products.read",
                    utcNow));
            }

            if (panel.Fiscal.RejectedCount > 0)
            {
                alerts.Add(Alert(
                    "nfe.rejected",
                    EnumNotificationSeverity.Critical,
                    "Notas rejeitadas",
                    $"{Titles(panel.Fiscal.RejectedCount, "nota foi rejeitada", "notas foram rejeitadas")} pela SEFAZ.",
                    "nfe.read",
                    utcNow));
            }

            if (panel.Fiscal.PendingCount > 0)
            {
                alerts.Add(Alert(
                    "nfe.pending",
                    EnumNotificationSeverity.Warning,
                    "Notas pendentes",
                    $"{Titles(panel.Fiscal.PendingCount, "nota está pendente", "notas estão pendentes")} de transmissão.",
                    "nfe.read",
                    utcNow));
            }

            if (panel.Fiscal.OutcomeUnknownCount > 0)
            {
                alerts.Add(Alert(
                    "nfe.outcomeUnknown",
                    EnumNotificationSeverity.Critical,
                    "Notas sem resposta da SEFAZ",
                    $"{Titles(panel.Fiscal.OutcomeUnknownCount, "nota foi transmitida", "notas foram transmitidas")} " +
                    "e a SEFAZ nunca respondeu.",
                    "nfe.read",
                    utcNow));
            }

            // Null is not zero: no certificate configured is a different fact from one about to
            // expire, and the panel already reports it through CertificateConfigured.
            if (panel.Fiscal.CertificateDaysRemaining is int daysRemaining && daysRemaining <= CertificateWarningDays)
            {
                alerts.Add(Alert(
                    "fiscal.certExpiring",
                    EnumNotificationSeverity.Critical,
                    daysRemaining < 0 ? "Certificado vencido" : "Certificado a vencer",
                    daysRemaining < 0
                        ? $"O certificado digital venceu há {Titles(-daysRemaining, "dia", "dias")}."
                        : daysRemaining == 0
                            ? "O certificado digital vence hoje."
                            : $"O certificado digital vence em {Titles(daysRemaining, "dia", "dias")}.",
                    "nfe.certificate.read",
                    utcNow));
            }

            if (panel.Bank.UnclearedCount > 0)
            {
                alerts.Add(Alert(
                    "bank.uncleared",
                    EnumNotificationSeverity.Info,
                    "Extrato a conciliar",
                    $"{Titles(panel.Bank.UnclearedCount, "lançamento bancário aguarda", "lançamentos bancários aguardam")} conciliação.",
                    "bank-reconciliation.read",
                    utcNow));
            }

            return alerts;
        }

        /// <summary>
        /// The firing keys, ordered, as one string. The sweep hashes this to decide whether anything
        /// changed — deliberately the keys and not the counts, so an overdue count moving from 3 to
        /// 4 does not re-ping every open browser in the company.
        /// </summary>
        public static string Fingerprint(IEnumerable<NotificationItemDTO> alerts) =>
            string.Join('|', alerts.Select(alert => alert.Key).OrderBy(key => key, StringComparer.Ordinal));

        private static NotificationItemDTO Alert(
            string key,
            EnumNotificationSeverity severity,
            string title,
            string body,
            string requiredPermission,
            DateTime utcNow) =>
            new()
            {
                Id = null,
                Source = NotificationSources.Alert,
                Key = key,
                Severity = severity,
                Title = title,
                Body = body,
                CreatedAtUtc = utcNow,
                IsRead = false,
                RequiredPermission = requiredPermission,
            };

        private static string Money(decimal amount) => amount.ToString("C", Brazil);

        private static string Titles(int count, string singular, string plural) =>
            count == 1 ? $"1 {singular}" : $"{count} {plural}";
    }
}
