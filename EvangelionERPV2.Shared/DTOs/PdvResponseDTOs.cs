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
}
