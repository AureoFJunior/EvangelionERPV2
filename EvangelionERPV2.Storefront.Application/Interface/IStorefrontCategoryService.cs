using EvangelionERPV2.StorefrontModule.Application.DTOs;

namespace EvangelionERPV2.StorefrontModule.Application.Interface
{
    /// <summary>Limen's platform-wide category tree (SPEC-limen-categories.md). Refusals throw <see cref="ArgumentException"/> with a pt-BR message.</summary>
    public interface IStorefrontCategoryService
    {
        Task<IReadOnlyList<StorefrontCategoryNodeDTO>> GetTreeAsync(Guid enterpriseId, bool includeInactive);

        Task<StorefrontCategoryNodeDTO> CreateAsync(SaveStorefrontCategoryRequestDTO request);

        Task<StorefrontCategoryNodeDTO?> UpdateAsync(Guid id, SaveStorefrontCategoryRequestDTO request);

        Task<StorefrontCategoryNodeDTO?> MoveAsync(Guid id, MoveStorefrontCategoryRequestDTO request);

        Task<StorefrontCategoryNodeDTO?> SetActiveAsync(Guid id, bool active);

        Task<int> AssignAsync(Guid enterpriseId, IReadOnlyCollection<Guid> productIds, Guid categoryId);

        Task EnsureAssignableAsync(Guid categoryId);

        Task<IReadOnlyList<StorefrontPublicCategoryDTO>> GetPublicTreeAsync();

        Task<StorefrontCategoryPageDTO?> GetPublicPageAsync(string slug);
    }
}
