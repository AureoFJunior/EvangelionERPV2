namespace EvangelionERPV2.StorefrontModule.Application.DTOs
{
    /// <summary>
    /// A node of the tree as ERP staff see it, with the active products under it (descendants included):
    /// the caller's own, or — for the Limen team — every store's, and how many stores they belong to.
    /// </summary>
    public sealed record StorefrontCategoryNodeDTO(
        Guid Id,
        Guid? ParentId,
        string Name,
        string Slug,
        string Icon,
        string? Description,
        int SortOrder,
        bool IsActive,
        int ProductCount,
        int StoreCount,
        IReadOnlyList<StorefrontCategoryNodeDTO> Children);

    /// <summary>The tree for ERP staff; <c>IsPlatformTeam</c> says whether the caller's enterprise edits it (Admins only, by role).</summary>
    public sealed record StorefrontCategoryTreeDTO(bool IsPlatformTeam, IReadOnlyList<StorefrontCategoryNodeDTO> Nodes);

    /// <summary>
    /// A node as Limen shows it: active only, with the number of published product cards (variant families
    /// count once) under it, descendants included. Limen hides a node whose count is zero.
    /// </summary>
    public sealed record StorefrontPublicCategoryDTO(
        string Slug,
        string Name,
        string Icon,
        string? Description,
        int ProductCount,
        IReadOnlyList<StorefrontPublicCategoryDTO> Children);

    /// <summary>Where a product sits, department first, for breadcrumbs.</summary>
    public sealed record StorefrontCategoryPathItemDTO(string Slug, string Name);

    /// <summary>A category page: the node, its path, and — when the slug was an old one — where it lives now.</summary>
    public sealed record StorefrontCategoryPageDTO(StorefrontPublicCategoryDTO Category, IReadOnlyList<StorefrontCategoryPathItemDTO> Path, string? RedirectTo);

    public sealed class SaveStorefrontCategoryRequestDTO
    {
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Icon { get; set; } = "Tag";
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class MoveStorefrontCategoryRequestDTO
    {
        public Guid? ParentId { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class SetProductCategoryRequestDTO
    {
        public List<Guid> ProductIds { get; set; } = [];
        public Guid CategoryId { get; set; }
    }
}
