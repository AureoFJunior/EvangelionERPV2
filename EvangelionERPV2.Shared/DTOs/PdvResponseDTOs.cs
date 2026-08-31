namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CashSessionDTO
    {
        public Guid Id { get; set; }
        public string TerminalId { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public decimal OpeningFloat { get; set; }
        public decimal? CountedAmount { get; set; }
        public decimal? ExpectedAmount { get; set; }
        public decimal? Difference { get; set; }
        public int Status { get; set; }
    }

    public sealed class CashMovementDTO
    {
        public Guid Id { get; set; }
        public Guid CashSessionId { get; set; }
        public int Type { get; set; }
        public decimal Amount { get; set; }
        public string? Reason { get; set; }
        public Guid UserId { get; set; }
        public Guid? ReferenceId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Tendered total for one payment method across a session.</summary>
    public sealed class PdvTenderTotalDTO
    {
        public int Method { get; set; }
        public decimal Amount { get; set; }
        public int SaleCount { get; set; }
    }

    /// <summary>
    /// The X-reading while a session is open, and the Z-reading once it is closed.
    /// </summary>
    /// <remarks>
    /// <see cref="ExpectedCash"/> is withheld while the session is open and the caller has not yet
    /// counted, because an operator who can see the target will hit the target — that is the whole
    /// value of a blind close.
    /// </remarks>
    public sealed class CashSessionSummaryDTO
    {
        public CashSessionDTO Session { get; set; } = new();
        public int SaleCount { get; set; }
        public decimal GrossSales { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalSurcharge { get; set; }
        public decimal CashIn { get; set; }
        public decimal CashOut { get; set; }

        /// <summary>Null while the session is open and uncounted. See the remarks on this type.</summary>
        public decimal? ExpectedCash { get; set; }

        public IEnumerable<PdvTenderTotalDTO> Tenders { get; set; } = [];
        public IEnumerable<CashMovementDTO> Movements { get; set; } = [];
    }

    /// <summary>What the server did with one synced sale.</summary>
    public sealed class PdvSaleSyncResultDTO
    {
        /// <summary>Echoed back so a batch response can be matched to the outbox rows that produced it.</summary>
        public Guid ClientSaleId { get; set; }

        public Guid PdvSaleId { get; set; }
        public Guid? OrderId { get; set; }

        /// <summary><see cref="Enums.EnumPdvSaleStatus"/>.</summary>
        public int Status { get; set; }

        /// <summary>
        /// True when this call found the sale already recorded and returned it rather than creating
        /// it. The terminal treats both the same — it just drops the outbox row — but a till that
        /// reports nothing but duplicates has a bug worth seeing.
        /// </summary>
        public bool WasAlreadyRecorded { get; set; }

        /// <summary>
        /// Set when <see cref="Status"/> is Rejected. A sentence, because the operator reads it.
        /// A rejected sale is final: the terminal must stop retrying and raise it with a human.
        /// </summary>
        public string? RejectReason { get; set; }
    }

    public sealed class PdvSaleSyncBatchResultDTO
    {
        public IEnumerable<PdvSaleSyncResultDTO> Results { get; set; } = [];
    }

    /// <summary>
    /// One product as the till needs it: enough to scan, price, sell and later emit fiscally,
    /// and nothing else.
    /// </summary>
    public sealed class PdvCatalogItemDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Cean { get; set; }
        public string? Sku { get; set; }
        public decimal DefaultValue { get; set; }
        public decimal StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = "Unit";
        public bool IsService { get; set; }
        public bool? IsActive { get; set; }

        /// <summary>Needed at the till only to build an NFC-e line offline.</summary>
        public string? Ncm { get; set; }
        public string? Cest { get; set; }
        public string? CfopIntra { get; set; }
        public byte? Origem { get; set; }
        public string? UnidadeTributavel { get; set; }

        /// <summary>
        /// Server clock when this row last changed, which is what the terminal sends back as its
        /// delta cursor. Not the terminal's clock: a till whose date is wrong would otherwise skip
        /// every price change made while it was drifting.
        /// </summary>
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>
    /// A page of catalog changes plus the cursor to resume from.
    /// </summary>
    /// <remarks>
    /// The cursor is server time, echoed rather than computed at the till, for the reason on
    /// <see cref="PdvCatalogItemDTO.ChangedAt"/>. <see cref="HasMore"/> lets a terminal that has
    /// been off for a month pull the catalog in pages instead of one enormous response.
    /// </remarks>
    public sealed class PdvCatalogPageDTO
    {
        public IEnumerable<PdvCatalogItemDTO> Items { get; set; } = [];
        public DateTime ServerTimeUtc { get; set; }
        public DateTime? NextCursor { get; set; }
        public bool HasMore { get; set; }
    }

    /// <summary>
    /// One product's thumbnail, and the hash that lets a till skip it next time.
    /// </summary>
    /// <remarks>
    /// A product with no usable picture is returned with an empty <see cref="ThumbnailBase64"/> and
    /// a null <see cref="Hash"/> rather than being left out of the page. The till records "there is
    /// no picture here" and stops asking; omitting it would make every sync try again forever.
    /// </remarks>
    public sealed class PdvProductImageDTO
    {
        public Guid ProductId { get; set; }
        public string? Hash { get; set; }
        public string ThumbnailBase64 { get; set; } = string.Empty;
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>A page of thumbnails plus the cursor to resume from. Mirrors <see cref="PdvCatalogPageDTO"/>.</summary>
    public sealed class PdvProductImagePageDTO
    {
        public IEnumerable<PdvProductImageDTO> Items { get; set; } = [];
        public DateTime ServerTimeUtc { get; set; }
        public DateTime? NextCursor { get; set; }
        public bool HasMore { get; set; }
    }

    /// <summary>
    /// The credential for a newly enrolled till.
    /// </summary>
    /// <remarks>
    /// Returned exactly once. Only a PBKDF2 verifier is stored, so nothing can retrieve this value
    /// afterwards — a lost credential means re-enrolling the terminal, which is the correct cost of
    /// storing it properly.
    /// </remarks>
    public sealed class EnrollTerminalResponseDTO
    {
        public string TerminalId { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
    }

    /// <summary>
    /// What a till gets back from a successful login.
    /// </summary>
    /// <remarks>
    /// <c>Permissions</c> is the operator's <c>pdv.*</c> grants and nothing else — the till caches
    /// them so it can grey out a button while offline. They are a mirror of the server's decision,
    /// never the decision itself: every call is still authorised server-side.
    /// </remarks>
    public sealed class PdvLoginResponseDTO
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public Guid EnterpriseId { get; set; }
        public IReadOnlyList<string> Permissions { get; set; } = [];
    }

    /// <summary>
    /// One supervisor a till may ask to authorise an override, and the verifier it checks the PIN
    /// against.
    /// </summary>
    /// <remarks>
    /// The verifier travels to the terminal on purpose: an override has to work with the network
    /// down, which is the case it exists for. A verifier is not a PIN — 210,000 PBKDF2 iterations
    /// is what stands between a stolen terminal database and six digits.
    /// </remarks>
    public sealed class SupervisorVerifierDTO
    {
        public Guid UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string PinVerifier { get; set; } = string.Empty;
        public IReadOnlyList<string> Permissions { get; set; } = [];
    }

    /// <summary>
    /// Everything a till needs to decide, on its own, who may do what.
    /// </summary>
    /// <remarks>
    /// Refreshed on every sync rather than at login: a terminal can run for days on one token, and
    /// a till that never hears about a sacking keeps honouring that person's overrides.
    ///
    /// <c>ServerTimeUtc</c> is what the terminal measures its own staleness against — a shop-floor
    /// clock is nobody's source of truth.
    /// </remarks>
    public sealed class PdvSyncStateDTO
    {
        public DateTime ServerTimeUtc { get; set; }
        public IReadOnlyList<PdvOperatorDTO> Operators { get; set; } = [];

        /// <summary>
        /// Refused overrides from across the estate, inside the server's recent window. The till
        /// counts these so a supervisor's lockout follows them from one terminal to the next.
        /// </summary>
        public IReadOnlyList<PdvFailedOverrideDTO> FailedOverrides { get; set; } = [];

        /// <summary>Users the till must purge. Absence from <c>Operators</c> is not enough: a till syncing a partial page would read it as "not mentioned" rather than "revoked".</summary>
        public IReadOnlyList<Guid> RevokedUserIds { get; set; } = [];

        /// <summary>
        /// Whether confirmed PIX is available at this shop's tills right now.
        /// </summary>
        /// <remarks>
        /// Carried on the snapshot the till already pulls every sync rather than on a new endpoint:
        /// the terminal must be able to grey the button out and say why *before* an operator presses
        /// it, and asking a server at press time cannot answer when the reason is "no connection".
        /// </remarks>
        public bool PixEnabled { get; set; }
    }

    /// <summary>
    /// One operator a till knows about, and what they may do at it.
    /// </summary>
    public sealed class PdvOperatorDTO
    {
        public Guid UserId { get; set; }
        public Guid EnterpriseId { get; set; }

        /// <summary>What the operator types at the till. The terminal's offline cache keys on it.</summary>
        public string UserName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
        public IReadOnlyList<string> Permissions { get; set; } = [];

        /// <summary>
        /// Null for everyone who cannot authorise an override. Only a supervisor's verifier travels
        /// to a shop floor; a cashier is listed so the till knows what to grey out, nothing more.
        /// </summary>
        public string? PinVerifier { get; set; }
    }

    /// <summary>What the ERP recorded for an override, and whether it had already been told.</summary>
    public sealed class RegisterOverrideResponseDTO
    {
        public Guid ClientOverrideId { get; set; }

        /// <summary>True when this id was already on file — a till resending its backlog, not a second override.</summary>
        public bool WasAlreadyRecorded { get; set; }
    }

    /// <summary>
    /// A refused override, as the till is told about it.
    /// </summary>
    /// <remarks>
    /// The lockout counts per supervisor rather than per terminal, or the guesser simply walks to
    /// the next till. A terminal only ever sees its own refusals, so the server ships back what
    /// happened on the others.
    /// </remarks>
    public sealed class PdvFailedOverrideDTO
    {
        public Guid SupervisorUserId { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
