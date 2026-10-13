namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class QuoteItemRequestDTO
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>
        /// The price offered. Zero or absent falls back to the product's default value — quoting a
        /// discount is most of the point, so a lower number here is honoured, not corrected.
        /// </summary>
        public decimal UnitValue { get; set; }
    }

    /// <summary>Create or edit a quote. Editing is only allowed while it is still a draft.</summary>
    public sealed class UpsertQuoteRequestDTO
    {
        public Guid? Id { get; set; }
        public Guid? CustomerId { get; set; }

        /// <summary>Last day the price stands. Defaults to fifteen days out when absent.</summary>
        public DateTime? ValidUntil { get; set; }

        public string? Notes { get; set; }
        public IEnumerable<QuoteItemRequestDTO> Items { get; set; } = [];
    }

    /// <summary>Move a quote along: sent, accepted, rejected.</summary>
    public sealed class QuoteStatusRequestDTO
    {
        public int Status { get; set; }
    }

    /// <summary>Turn an accepted quote into a real order.</summary>
    public sealed class ConvertQuoteRequestDTO
    {
        /// <summary>When the resulting order expects to be paid. Defaults to thirty days out.</summary>
        public DateTime? PaymentScheduledDate { get; set; }
    }

    public sealed class QuoteItemDTO : BaseDTO
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitValue { get; set; }
        public decimal LineAmount { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
    }

    public sealed class QuoteDTO : BaseDTO
    {
        public long Number { get; set; }
        public Guid EnterpriseId { get; set; }
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;

        /// <summary>The stored status.</summary>
        public int Status { get; set; }

        /// <summary>
        /// The status as of now, which is <c>Expired</c> once ValidUntil has passed whatever the
        /// column says — the same way an unpaid boleto reads as overdue without a sweeper.
        /// </summary>
        public int EffectiveStatus { get; set; }

        public DateTime IssuedAt { get; set; }
        public DateTime ValidUntil { get; set; }
        public decimal TotalValue { get; set; }
        public string? Notes { get; set; }

        /// <summary>The order this quote became, once it has been converted.</summary>
        public Guid? ConvertedOrderId { get; set; }

        public List<QuoteItemDTO> Items { get; set; } = [];
    }
}
