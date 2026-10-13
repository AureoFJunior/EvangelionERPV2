namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>One month of selling, per seller, with the company's own row alongside.</summary>
    public sealed class SalesPerformanceDTO
    {
        public int Year { get; set; }
        public int Month { get; set; }

        public IEnumerable<SalesPerformanceRowDTO> Sellers { get; set; } = Array.Empty<SalesPerformanceRowDTO>();

        /// <summary>
        /// The whole enterprise for the month, against the enterprise-wide target. Not the sum of
        /// the seller rows: an order placed by nobody in particular (the queued path has no user
        /// behind it) belongs to the company's number but to no seller's.
        /// </summary>
        public SalesPerformanceRowDTO Enterprise { get; set; } = new();
    }

    public sealed class SalesPerformanceRowDTO
    {
        /// <summary>Null on the enterprise row.</summary>
        public Guid? UserId { get; set; }

        public string SellerName { get; set; } = string.Empty;

        /// <summary>Booked in the month, refunds excluded.</summary>
        public decimal SoldAmount { get; set; }

        /// <summary>Actually collected. The commission basis.</summary>
        public decimal ReceivedAmount { get; set; }

        public int OrderCount { get; set; }

        /// <summary>Null when no target was set for this seller and month.</summary>
        public decimal? TargetAmount { get; set; }

        /// <summary>Sold over target, as a percentage. Null without a target.</summary>
        public decimal? AttainmentPercent { get; set; }

        /// <summary>The seller's rate as a fraction — 0.05 is 5%. Null when none is set.</summary>
        public decimal? CommissionRate { get; set; }

        /// <summary>Rate applied to <see cref="ReceivedAmount"/>. Null when no rate is set.</summary>
        public decimal? CommissionAmount { get; set; }
    }

    public sealed class SalesGoalDTO
    {
        public Guid Id { get; set; }
        public Guid? UserId { get; set; }
        public string? SellerName { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal TargetAmount { get; set; }
    }

    /// <summary>
    /// Upsert by (seller, competência) rather than by id: the screen edits a cell in a month grid
    /// and has no id to send the first time a target is set.
    /// </summary>
    public sealed class UpsertSalesGoalRequestDTO
    {
        /// <summary>Null sets the enterprise-wide target.</summary>
        public Guid? UserId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal TargetAmount { get; set; }
    }
}
