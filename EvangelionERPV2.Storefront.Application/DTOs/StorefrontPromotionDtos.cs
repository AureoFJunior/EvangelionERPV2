namespace EvangelionERPV2.StorefrontModule.Application.DTOs
{
    /// <summary>
    /// A product's "de/por" state for the store (SPEC-limen-pricing-rules §2): what it has, and the "de"
    /// prices its own history makes eligible (P2), highest first.
    /// </summary>
    public sealed record StorefrontPromotionDTO(
        Guid ProductId,
        decimal Price,
        decimal? CompareAtPrice,
        int? DiscountPercent,
        DateTime? StartedAt,
        DateTime? EndsAt,
        bool Active,
        IReadOnlyList<decimal> EligibleCompareAtPrices);

    public sealed class SetStorefrontPromotionRequestDTO
    {
        public Guid ProductId { get; set; }
        public decimal CompareAtPrice { get; set; }
        public DateTime EndsAt { get; set; }

        /// <summary>P7: "Declaro que pratiquei o preço de R$ X nos últimos 30 dias."</summary>
        public bool Attested { get; set; }
    }
}
