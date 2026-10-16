using EvangelionERPV2.StorefrontModule.Application.DTOs;

namespace EvangelionERPV2.StorefrontModule.Application.Interface
{
    /// <summary>
    /// The storefront's view of the ERP. Catalog reads are cross-tenant by design and see only listed
    /// enterprises and published products; the listing methods act on one tenant, the caller's.
    /// </summary>
    public interface IStorefrontService
    {
        Task<StorefrontPageDTO<StorefrontEnterpriseSummaryDTO>> GetEnterprisesAsync(int page, int size);

        Task<StorefrontEnterpriseProfileDTO?> GetEnterpriseAsync(string slug);

        Task<StorefrontPageDTO<StorefrontProductSummaryDTO>> GetProductsAsync(StorefrontProductQuery query);

        Task<StorefrontProductDetailDTO?> GetProductAsync(Guid id);

        Task<StorefrontPictureDTO?> GetPictureAsync(Guid productId);
        Task<StorefrontPictureDTO?> GetProductImageAsync(Guid imageId);

        Task<StorefrontPictureDTO?> GetFamilyPictureAsync(Guid variantGroupId);

        Task<StorefrontListingDTO> GetListingAsync(Guid enterpriseId);

        /// <summary>Saves the profile; lists or unlists. Throws <see cref="ArgumentException"/> with a pt-BR message when refused.</summary>
        Task<StorefrontListingDTO> SetListingAsync(Guid enterpriseId, SetStorefrontListingRequestDTO request);
        /// <summary>Replaces the shop's logo or cover; throws ArgumentException unless it is PNG, JPEG or WebP up to 5 MB.</summary>
        Task<StorefrontListingDTO> SetListingImageAsync(Guid enterpriseId, StorefrontImageKind kind, string? base64File);
        Task<StorefrontListingDTO> RemoveListingImageAsync(Guid enterpriseId, StorefrontImageKind kind);
        /// <summary>A listed shop's logo or cover, for Limen; null when it has none or is not listed.</summary>
        Task<StorefrontPictureDTO?> GetEnterpriseImageAsync(string slug, StorefrontImageKind kind);
        /// <summary>The caller's own shop image, listed or not, for the settings preview.</summary>
        Task<StorefrontPictureDTO?> GetListingImageAsync(Guid enterpriseId, StorefrontImageKind kind);

        /// <summary>Publishes or withdraws the caller's own products. Answers how many rows changed.</summary>
        Task<int> SetPublishedAsync(Guid enterpriseId, IReadOnlyCollection<Guid> productIds, bool published);
    }
}
