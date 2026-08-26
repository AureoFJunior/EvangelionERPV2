namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>Opens a till. The terminal names itself; the ERP never provisions terminals.</summary>
    public sealed class OpenCashSessionRequestDTO
    {
        /// <summary>The terminal's own id for the session. Makes a re-sent open idempotent.</summary>
        public Guid ClientSessionId { get; set; }

        public string TerminalId { get; set; } = string.Empty;
        public decimal OpeningFloat { get; set; }

        /// <summary>
        /// When the operator actually opened the drawer, in UTC. Sent by the terminal because a
        /// session opened offline reaches the server long after the fact.
        /// </summary>
        public DateTime OpenedAt { get; set; }
    }

    /// <summary>
    /// Closes a till with a blind count: the operator sends what they counted and only then is
    /// told what was expected.
    /// </summary>
    public sealed class CloseCashSessionRequestDTO
    {
        public Guid ClientSessionId { get; set; }
        public decimal CountedAmount { get; set; }
        public DateTime ClosedAt { get; set; }
    }

    /// <summary>A sangria or suprimento. Sales never come through here — they arrive with the sale.</summary>
    public sealed class CashMovementRequestDTO
    {
        public Guid ClientMovementId { get; set; }

        public Guid ClientSessionId { get; set; }

        /// <summary><see cref="Enums.EnumCashMovementType"/>. Only Sangria and Suprimento are accepted.</summary>
        public int Type { get; set; }

        /// <summary>Always positive. The service applies the sign that the type implies.</summary>
        public decimal Amount { get; set; }

        public string? Reason { get; set; }

        /// <summary>When it happened at the till, in UTC. Not when the server heard about it.</summary>
        public DateTime OccurredAt { get; set; }
    }

    public sealed class PdvSaleItemRequestDTO
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>
        /// Unit price actually charged, discount already applied. The order total is
        /// Σ(Value × Quantity) and nothing may make that untrue, so a discount lives in the price
        /// here and is reported separately on the sale.
        /// </summary>
        public decimal Value { get; set; }
    }

    public sealed class PdvSalePaymentRequestDTO
    {
        /// <summary><see cref="Enums.EnumPaymentMethod"/>.</summary>
        public int Method { get; set; }

        /// <summary>What this tender settled — not what the customer handed over.</summary>
        public decimal Amount { get; set; }

        /// <summary>Change handed back. Cash only.</summary>
        public decimal ChangeGiven { get; set; }

        public string? AuthorizationCode { get; set; }
        public int? Installments { get; set; }
    }

    /// <summary>
    /// A finished sale on its way up from a terminal. Safe to send more than once.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientSaleId"/> is minted at the till before the customer leaves. A retry after a
    /// lost response carries the same value and returns the order the first attempt created, which
    /// is the only way a till on a bad connection can avoid selling the same basket twice.
    /// </remarks>
    public sealed class PdvSaleSyncRequestDTO
    {
        public Guid ClientSaleId { get; set; }

        /// <summary>The session as the terminal knows it — see <c>CashSession.ClientSessionId</c>.</summary>
        public Guid ClientSessionId { get; set; }

        public string TerminalId { get; set; } = string.Empty;

        /// <summary>When the customer paid, per the terminal clock, in UTC.</summary>
        public DateTime SoldAtLocal { get; set; }

        /// <summary>Null for an anonymous walk-in, which is most of them.</summary>
        public Guid? CustomerId { get; set; }

        public decimal DiscountAmount { get; set; }
        public decimal SurchargeAmount { get; set; }

        public IEnumerable<PdvSaleItemRequestDTO> Items { get; set; } = [];
        public IEnumerable<PdvSalePaymentRequestDTO> Payments { get; set; } = [];
    }

    /// <summary>A batch of sales drained from a terminal's outbox after it came back online.</summary>
    public sealed class PdvSaleSyncBatchRequestDTO
    {
        public IEnumerable<PdvSaleSyncRequestDTO> Sales { get; set; } = [];
    }

    /// <summary>Enrolls one till and mints its credential.</summary>
    public sealed class EnrollTerminalRequestDTO
    {
        public string TerminalId { get; set; } = string.Empty;
    }
}
