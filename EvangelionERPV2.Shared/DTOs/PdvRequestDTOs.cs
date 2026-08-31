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

        /// <summary>The charge the till believes settled this tender. Null for every unproven tender.</summary>
        public Guid? ChargeId { get; set; }

        /// <summary>
        /// What the till claims. Advisory only — see <c>PdvService.CompleteAsync</c>, which will not
        /// call a tender proven unless it names a charge.
        /// </summary>
        public bool IsVerified { get; set; }
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

    /// <summary>
    /// An operator logging in at a till.
    /// </summary>
    /// <remarks>
    /// Deliberately carries no reCAPTCHA field: a desktop terminal has no browser and cannot solve
    /// one. The enrolled terminal credential in the <c>X-Pdv-Terminal</c> header is what replaces
    /// the human-presence check, which is why that header is not optional.
    /// </remarks>
    public sealed class PdvLoginRequestDTO
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// A user setting the PIN that authorises overrides at a till.
    /// </summary>
    /// <remarks>
    /// Self-service: the PIN is the user's own, and only its verifier is ever stored. Six digits is
    /// the floor because the verifier is handed to terminals, where a shorter one is guessable
    /// offline no matter how many PBKDF2 iterations sit behind it.
    /// </remarks>
    public sealed class SetPdvPinRequestDTO
    {
        public string Pin { get; set; } = string.Empty;
    }

    /// <summary>
    /// One override a till is reporting: a supervisor was asked to authorise something the operator
    /// could not do alone.
    /// </summary>
    /// <remarks>
    /// Refused attempts are reported too, and matter more — see <c>PdvOverrideAudit</c>. The PIN
    /// itself never leaves the terminal; the till checks it against the cached verifier and sends
    /// only the outcome.
    /// </remarks>
    public sealed class RegisterOverrideRequestDTO
    {
        /// <summary>The override as the terminal numbered it. A resend with the same id records once.</summary>
        public Guid ClientOverrideId { get; set; }

        public Guid SupervisorUserId { get; set; }
        public string TerminalId { get; set; } = string.Empty;

        /// <summary>The basket it happened in. Empty when no sale was open.</summary>
        public Guid ClientSaleId { get; set; }

        public string Permission { get; set; } = string.Empty;
        public bool Succeeded { get; set; }

        /// <summary>When it happened, per the terminal clock, in UTC.</summary>
        public DateTime OccurredAt { get; set; }
    }
}
