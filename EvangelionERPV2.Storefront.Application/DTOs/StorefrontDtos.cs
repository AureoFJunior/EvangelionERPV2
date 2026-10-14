namespace EvangelionERPV2.StorefrontModule.Application.DTOs
{
    // The public contract of api/v1/Storefront/* (SPEC-ecommerce-storefront.md §4.3–4.4). Never add an
    // entity field here without asking whether a stranger on the internet may read it; the snapshot
    // test in StorefrontModule.Test fails on any new property so that question gets asked.

    public sealed record StorefrontPageDTO<T>(IReadOnlyList<T> Items, int Total);

    public record StorefrontEnterpriseSummaryDTO(
        Guid Id,
        string Slug,
        string Name,
        string? LogoUrl,
        string Description,
        string City,
        string Uf,
        int ProductCount,
        string? LogoVersion,
        string? CoverVersion);

    /// <summary>
    /// A listed shop with its legal identity (Decreto 7.962 art. 2 I) and the terms of its offer.
    /// Delivery days are handling + transit, already summed.
    /// </summary>
    public sealed record StorefrontEnterpriseProfileDTO(
        Guid Id,
        string Slug,
        string Name,
        string? LogoUrl,
        string Description,
        string City,
        string Uf,
        int ProductCount,
        string LegalName,
        string Cnpj,
        string Email,
        string Phone,
        decimal ShippingFee,
        int DeliveryDaysMin,
        int DeliveryDaysMax,
        DateOnly ListedSince,
        string? LogoVersion,
        string? CoverVersion)
        : StorefrontEnterpriseSummaryDTO(Id, Slug, Name, LogoUrl, Description, City, Uf, ProductCount, LogoVersion, CoverVersion);

    /// <summary>
    /// One card per variant group: <see cref="Id"/> is the group's representative row, <see cref="Price"/>
    /// the group's lowest, <see cref="PriceFrom"/> whether other variants cost more. <see cref="CompareAtPrice"/>
    /// is the "de" of the row at that lowest price, only while its promotion is valid (SPEC-limen-pricing-rules
    /// §2); <see cref="MaxDiscountPercent"/> the family's best ("até −Z%").
    /// </summary>
    public record StorefrontProductSummaryDTO(
        Guid Id,
        string Name,
        decimal Price,
        bool HasPicture,
        bool Available,
        string EnterpriseName,
        string EnterpriseSlug,
        DateTime CreatedAt,
        bool PriceFrom,
        int VariantCount,
        Guid? PictureProductId = null,
        Guid? FamilyPictureId = null,
        decimal? CompareAtPrice = null,
        int? DiscountPercent = null,
        int? MaxDiscountPercent = null,
        StorefrontInstallmentDTO? MaxInstallment = null,
        bool LowStock = false);

    /// <summary>One row of a group, as the picker shows it. Unpublished rows come as unavailable.</summary>
    public sealed record StorefrontVariantDTO(Guid Id, string Label, decimal Price, bool Available, bool HasPicture = false);

    public sealed record StorefrontProductDetailDTO(
        Guid Id,
        string Name,
        decimal Price,
        bool HasPicture,
        bool Available,
        string EnterpriseName,
        string EnterpriseSlug,
        DateTime CreatedAt,
        bool PriceFrom,
        int VariantCount,
        string Description,
        int MaxPerOrder,
        string UnitOfMeasure,
        StorefrontEnterpriseProfileDTO Enterprise,
        IReadOnlyList<StorefrontVariantDTO> Variants,
        Guid SelectedVariantId,
        Guid? PictureProductId = null,
        Guid? FamilyPictureId = null,
        IReadOnlyList<StorefrontCategoryPathItemDTO>? CategoryPath = null,
        decimal? CompareAtPrice = null,
        int? DiscountPercent = null,
        DateTime? PromotionEndsAt = null,
        IReadOnlyList<StorefrontInstallmentDTO>? Installments = null,
        bool LowStock = false)
        : StorefrontProductSummaryDTO(Id, Name, Price, HasPicture, Available, EnterpriseName, EnterpriseSlug, CreatedAt, PriceFrom, VariantCount,
            PictureProductId, FamilyPictureId, CompareAtPrice, DiscountPercent, MaxInstallment: Installments is { Count: > 1 } plans ? plans[^1] : null,
            LowStock: LowStock);

    public enum StorefrontProductSort
    {
        Relevance,
        PriceAsc,
        PriceDesc,
        Newest,
        /// <summary>Units sold on Limen in the last 30 days, most first; products never sold there are left out.</summary>
        BestSelling
    }

    public sealed record StorefrontProductQuery(
        string? Q,
        string? EnterpriseSlug,
        decimal? MinPrice,
        decimal? MaxPrice,
        bool Available,
        StorefrontProductSort Sort,
        int Page,
        int Size,
        string? Category = null,
        bool OnSale = false);

    /// <summary>What ERP staff edit on the "Loja na Limen" screen.</summary>
    public sealed class StorefrontListingDTO
    {
        public bool IsListed { get; set; }
        public DateTime? ListedAt { get; set; }
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }

        /// <summary>Null when no logo was uploaded; otherwise changes with every upload, for cache busting.</summary>
        public string? LogoVersion { get; set; }
        public string? CoverVersion { get; set; }
        public decimal ShippingFee { get; set; }
        public int HandlingDays { get; set; }
        public int DeliveryDaysMin { get; set; }
        public int DeliveryDaysMax { get; set; }

        /// <summary>What still stops the shop from being listed, in pt-BR, empty when nothing does.</summary>
        public IReadOnlyList<string> MissingRequirements { get; set; } = [];
    }

    public sealed class SetStorefrontListingRequestDTO
    {
        public bool IsListed { get; set; }
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public decimal ShippingFee { get; set; }
        public int HandlingDays { get; set; }
        public int DeliveryDaysMin { get; set; }
        public int DeliveryDaysMax { get; set; }
    }

    public enum StorefrontImageKind { Logo, Cover }

    public sealed class UploadStorefrontImageRequestDTO
    {
        public string? Kind { get; set; }
        public string? File { get; set; }
    }

    public sealed class SetStorefrontPublishedRequestDTO
    {
        public IReadOnlyList<Guid> ProductIds { get; set; } = [];
        public bool Published { get; set; }
    }

    public sealed class StorefrontLoginRequestDTO
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }

    public sealed class StorefrontLoginResponseDTO
    {
        public string Token { get; set; } = string.Empty;
    }

    public sealed record StorefrontPictureDTO(byte[] Content, string ContentType);
}
