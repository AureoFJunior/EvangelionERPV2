namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Everything the morning check needs, in one response.
    /// </summary>
    public class DailyPanelDTO
    {
        public DateTime GeneratedAtUtc { get; set; }

        public DailyPanelReceivablesDTO Receivables { get; set; } = new();
        public DailyPanelPayablesDTO Payables { get; set; } = new();
        public DailyPanelStockDTO Stock { get; set; } = new();
        public DailyPanelFiscalDTO Fiscal { get; set; } = new();
        public DailyPanelBankDTO Bank { get; set; } = new();
    }

    public class DailyPanelReceivablesDTO
    {
        public int OverdueCount { get; set; }
        public decimal OverdueAmount { get; set; }
        public int DueTodayCount { get; set; }
        public decimal DueTodayAmount { get; set; }

        /// <summary>Open titles regardless of date — the whole carteira.</summary>
        public int OpenCount { get; set; }
        public decimal OpenAmount { get; set; }
    }

    public class DailyPanelPayablesDTO
    {
        public int OverdueCount { get; set; }
        public decimal OverdueAmount { get; set; }
        public int DueTodayCount { get; set; }
        public decimal DueTodayAmount { get; set; }

        /// <summary>Falling due in the next seven days, today included.</summary>
        public int DueThisWeekCount { get; set; }
        public decimal DueThisWeekAmount { get; set; }
    }

    public class DailyPanelStockDTO
    {
        /// <summary>Products the replenishment engine rates as high criticality.</summary>
        public int CriticalCount { get; set; }
        public List<string> CriticalProducts { get; set; } = [];

        /// <summary>True when the figures came from cache rather than a fresh ledger scan.</summary>
        public bool FromCache { get; set; }
    }

    public class DailyPanelFiscalDTO
    {
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }

        /// <summary>
        /// Transmitted, and SEFAZ never answered. An ops alarm, not a work queue: the
        /// reconciliation worker owns these and a human seeing a number here means it is stuck.
        /// </summary>
        public int OutcomeUnknownCount { get; set; }

        /// <summary>Null when no certificate is configured, which is itself worth showing.</summary>
        public int? CertificateDaysRemaining { get; set; }
        public bool CertificateConfigured { get; set; }
    }

    public class DailyPanelBankDTO
    {
        /// <summary>Statement lines still waiting on a human — unmatched plus ambiguous.</summary>
        public int UnclearedCount { get; set; }
    }
}
